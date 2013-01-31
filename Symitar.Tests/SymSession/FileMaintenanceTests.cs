﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Rhino.Mocks;
using Symitar.Interfaces;

namespace Symitar.Tests
{
    [TestFixture]
    public class FileMaintenanceTests
    {
        [Test]
        public void UnitOfWork_Scenario_ExpectedBehavior()
        {
            var mockSocket = MockRepository.GenerateMock<ISymSocket>();
            mockSocket.Stub(x => x.ReadCommand())
                      .Return(new SymCommand("FileList", new Dictionary<string, string> { { "Done", "" } }));

            SymSession session = new SymSession(mockSocket, 10);

            int result = session.GetFileMaintenanceSequence("Report Title");
            result.Should().Be(-1);
        }
    }
}