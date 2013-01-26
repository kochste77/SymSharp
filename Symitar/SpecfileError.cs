using System;
using System.Text;

namespace Symitar
{
    public class SpecfileError
    {
        private SymFile _sourceFile;
        public SymFile Specfile
        {
            get { return _sourceFile; }
        }

        private string _fileError;
        public string FileError
        {
            get { return _fileError; }
        }

        private string _errorMessage;
        public string ErrorMessage
        {
            get { return _errorMessage; }
        }

        private int _errorLine;
        private int _errorColumn;

        public int Line
        {
            get { return _errorLine; }
        }

        public int Column
        {
            get { return _errorColumn; }
        }

        public int InstallSize { get; set; }
        public bool InvalidInstall { get; set; }

        public bool FailedCheck
        {
            get { return InvalidInstall || _errorLine > 0; }
        }

        public SpecfileError(SymFile specfile, string fileError, string message, int line, int col)
        {
            _sourceFile = specfile;
            _fileError = fileError;
            _errorMessage = message;
            _errorLine = line;
            _errorColumn = col;
            InvalidInstall = false;
        }

        public static SpecfileError None()
        {
            SpecfileError error = new SpecfileError(null, "", "", 0, 0);
            return error;
        }

        public static SpecfileError None(int size)
        {
            if(size < 0)
                throw new ArgumentOutOfRangeException("size", "Specfile install size cannot be less than 0.");

            SpecfileError ret = new SpecfileError(null, "", "", 0, 0);
            ret.InstallSize = size;

            if (size == 0) ret.InvalidInstall = true;

            return ret;
        }
    }
}
