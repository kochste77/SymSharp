﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using Symitar.Interfaces;

namespace Symitar
{
    public partial class SymSession
    {
        public delegate string FileRunPrompt(string prompt);

        public enum RunState
        {
            Initializing,
            Prompts,
            Running,
            Complete,
            Failed,
            Cancelled
        }

        public delegate void FileRunStatus(RunState state, object data);

        public bool IsFileRunning(int sequence)
        {
            if (sequence <= 0)
                throw new ArgumentOutOfRangeException("sequence");

            ISymCommand cmd;
            bool running = false;

            cmd = new SymCommand("Misc");
            cmd.Set("InfoType", "BatchQueues");
            _socket.Write(cmd);

            cmd = _socket.ReadCommand();
            while (!cmd.HasParameter("Done"))
            {
                if ((cmd.Get("Action") == "QueueEntry") && (int.Parse(cmd.Get("Seq")) == sequence))
                    running = true;
                else if (cmd.Get("Action") == "Close")
                {
                    Reconnect();
                    cmd = new SymCommand("Misc");
                    cmd.Set("InfoType", "BatchQueues");
                    _socket.Write(cmd);
                }
                else if (cmd.Command == "")
                {
                    cmd = new SymCommand("Misc");
                    cmd.Set("InfoType", "BatchQueues");
                    _socket.Write(cmd);
                }

                cmd = _socket.ReadCommand();
            }

            return running;
        }

        private List<int> GetPrintSequences(string searchTerm)
        {
            var seqs = new List<int>();
            ISymCommand cmd;

            cmd = new SymCommand("File");
            cmd.Set("Action", "List");
            cmd.Set("MaxCount", "50");
            cmd.Set("Query", "LAST 20 \"+" + searchTerm + "+\"");
            cmd.Set("Type", "Report");
            _socket.Write(cmd);

            cmd = _socket.ReadCommand();
            while (!cmd.HasParameter("Done"))
            {
                if (cmd.HasParameter("Sequence"))
                    seqs.Add(int.Parse(cmd.Get("Sequence")));
                cmd = _socket.ReadCommand();
            }

            seqs.Sort();
            seqs.Reverse();
            return seqs;
        }

        public int GetBatchOutputSequence(string reportName, int time)
        {
            Reconnect();
            List<int> seqs = GetPrintSequences("REPWRITER");
            foreach (int i in seqs)
            {
                var file = new File(_socket.Server, SymDirectory.ToString(), i.ToString(), FileType.Report, DateTime.Now,
                                    0);
                string contents = FileRead(file);
                int beganIndex = contents.IndexOf("Processing begun on");
                if (beganIndex != -1)
                {
                    contents = contents.Substring(beganIndex + 41);
                    string timeStr = contents.Substring(0, 8);
                    string[] tokens = timeStr.Split(':');
                    string seconds = tokens[2], minutes = tokens[1], hours = tokens[0];
                    
                    int currTime = Utilities.ConvertTime(timeStr);

                    contents = contents.Substring(contents.IndexOf("(newline when done):") + 21);

                    string name = contents.Substring(0, contents.IndexOf('\n'));
                    if (name == reportName && Math.Abs(time - currTime) <= 1)
                        return i;
                }
            }
            return -1;
        }

        public List<int> GetReportSequences(int batchOutputSequence)
        {
            var file = new File(_socket.Server, SymDirectory.ToString(), batchOutputSequence.ToString(), FileType.Report, DateTime.Now,
                                0);
            var lines = FileRead(file).Split('\n');

            var sequences = (from line in lines where line.Contains("Seq:") && line.Contains("Title:") select int.Parse(line.Substring(7, 6))).ToList();

            return sequences;
        }

        public List<string> GetReportTitles(int batchOutputSequence)
        {
            var file = new File(_socket.Server, SymDirectory.ToString(), batchOutputSequence.ToString(), FileType.Report, DateTime.Now,
                                0);
            var lines = FileRead(file).Split('\n');

            var titles = (from line in lines where line.Contains("Seq:") && line.Contains("Title:") select line.Substring(line.IndexOf("Title:")+7)).ToList();

            return titles;
        }

        public RepgenRunResult FileRun(File file, FileRunStatus callStatus, FileRunPrompt callPrompt, int queue, RunWorkerCompletedEventHandler Notify = null)
        {
            if (file.Type != FileType.RepGen)
                throw new InvalidOperationException("Cannot run a " + file.FileTypeString() + " file");

            ISymCommand cmd;
            callStatus(RunState.Initializing, file);

            _socket.Write("mm0\u001B");

            WaitForCommand("Input");
            _socket.Write("1\r");

            WaitForCommand("Input");
            _socket.Write("11\r");

            WaitForPrompt("Specification File");
            _socket.Write(file.Name + "\r");
            bool erroredOut = false;
            while (true)
            {
                cmd = _socket.ReadCommand();

                if (cmd.Command == "Input")
                {
                    if (cmd.Get("HelpCode") == "20301") break;

                    callStatus(RunState.Prompts, file);

                    string result = callPrompt(cmd.Get("Prompt"));
                    if (result == null) //cancelled
                    {
                        _socket.Write("\u001B");
                        cmd = _socket.ReadCommand();
                        while (cmd.Command != "Input")
                            cmd = _socket.ReadCommand();
                        callStatus(RunState.Cancelled, file);
                        return RepgenRunResult.Cancelled();
                    }

                    _socket.Write(result.Trim() + '\r');
                }
                else if (cmd.Command == "Bell")
                    callStatus(RunState.Prompts, "Invalid Prompt Input, Please Re-Enter");
                else if ((cmd.Command == "Batch") && (cmd.Get("Text") == "No such file or directory"))
                {
                    cmd = _socket.ReadCommand();
                    while (cmd.Command != "Input")
                        cmd = _socket.ReadCommand();
                    callStatus(RunState.Failed, "File not found");
                    return RepgenRunResult.FileNotFound();
                }
                else if (cmd.Command == "SpecfileErr")
                    erroredOut = true;
                else if (erroredOut && (cmd.Command == "Batch") && (cmd.Get("Action") == "DisplayLine"))
                {
                    string err = cmd.Get("Text");
                    cmd = _socket.ReadCommand();
                    while (cmd.Command != "Input")
                        cmd = _socket.ReadCommand();
                    callStatus(RunState.Failed, err);
                    return RepgenRunResult.Error(err);
                }
                else if ((cmd.Command == "Batch") && (cmd.Get("Action") == "DisplayLine"))
                    callStatus(RunState.Initializing, cmd.Get("Text"));
            }

            while (cmd.Get("Prompt").Contains("Specification File"))
            {
                _socket.Write("\r");
                cmd = _socket.ReadCommand();
            }

            WaitForPrompt("Batch Options");
            _socket.Write("0\r");

            Dictionary<int, int> availableQueues = GetQueueList(cmd);

            if (queue < 0)
                queue = GetOpenQueue(availableQueues);

            WaitForPrompt("Batch Queue");
            _socket.Write(queue + "\r");

            WaitForCommand("Input");

            _socket.Write("1\r");
            cmd = _socket.ReadCommand();
            while (cmd.Command != "Input")
                cmd = _socket.ReadCommand();

            cmd = new SymCommand("Misc");
            cmd.Set("InfoType", "BatchQueues");
            _socket.Write(cmd);

            int newestTime = 0;
            int sequenceNo = -1;
            cmd = _socket.ReadCommand();
            while (!cmd.HasParameter("Done"))
            {
                if (cmd.Get("Action") == "QueueEntry")
                {
                    int currTime = Utilities.ConvertTime(cmd.Get("Time"));

                    if (currTime >= newestTime)
                    {
                        newestTime = currTime;
                        sequenceNo = int.Parse(cmd.Get("Seq"));
                    }
                }
                cmd = _socket.ReadCommand();
            }

            callStatus(RunState.Running, sequenceNo);

            if (Notify != null)
            {
                var worker = new BackgroundWorker();

                worker.DoWork += (sender, eventArgs) =>
                    {
                        Thread.Sleep(5000); // Wait 5 seconds before first check
                        while (IsFileRunning(sequenceNo))
                        {
                            Thread.Sleep(15000);
                        }

                        object[] result = new object[3];
                        result[0] = file.Name;
                        result[1] = sequenceNo;
                        result[2] = GetBatchOutputSequence(file.Name, newestTime);

                        eventArgs.Result = result;
                    };

                worker.RunWorkerCompleted += Notify;

                worker.RunWorkerAsync();
            }

            return RepgenRunResult.Okay(sequenceNo, newestTime);
        }
    }
}