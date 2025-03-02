﻿using System;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Renci.SshNet.Tests.Common;

namespace Renci.SshNet.Tests.Classes
{
    /// <summary>
    /// Provides functionality to perform private key authentication.
    /// </summary>
    [TestClass]
    public class PrivateKeyAuthenticationMethodTest : TestBase
    {
        [TestMethod]
        public void PrivateKey_Test_Pass_Null()
        {
            Assert.ThrowsException<ArgumentNullException>(() => new PrivateKeyAuthenticationMethod(null, null));
        }

        [TestMethod]
        public void PrivateKey_Test_Pass_PrivateKey_Null()
        {
            Assert.ThrowsException<ArgumentNullException>(() => new PrivateKeyAuthenticationMethod("username", null));
        }

        [TestMethod]
        public void PrivateKey_Test_Pass_Whitespace()
        {
            Assert.ThrowsException<ArgumentException>(() => new PrivateKeyAuthenticationMethod(string.Empty, null));
        }
    }
}
