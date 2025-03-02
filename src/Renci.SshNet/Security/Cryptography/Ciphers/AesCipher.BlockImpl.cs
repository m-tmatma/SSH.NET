﻿using System;
using System.Security.Cryptography;

using Org.BouncyCastle.Crypto.Paddings;

namespace Renci.SshNet.Security.Cryptography.Ciphers
{
    public partial class AesCipher
    {
        private sealed class BlockImpl : BlockCipher, IDisposable
        {
            private readonly Aes _aes;
            private readonly ICryptoTransform _encryptor;
            private readonly ICryptoTransform _decryptor;

            public BlockImpl(byte[] key, CipherMode mode, IBlockCipherPadding padding)
                : base(key, 16, mode, padding)
            {
                var aes = Aes.Create();
                aes.Key = key;
                aes.Mode = System.Security.Cryptography.CipherMode.ECB;
                aes.Padding = PaddingMode.None;
                _aes = aes;
                _encryptor = aes.CreateEncryptor();
                _decryptor = aes.CreateDecryptor();
            }

            public override int EncryptBlock(byte[] inputBuffer, int inputOffset, int inputCount, byte[] outputBuffer, int outputOffset)
            {
                return _encryptor.TransformBlock(inputBuffer, inputOffset, inputCount, outputBuffer, outputOffset);
            }

            public override int DecryptBlock(byte[] inputBuffer, int inputOffset, int inputCount, byte[] outputBuffer, int outputOffset)
            {
                return _decryptor.TransformBlock(inputBuffer, inputOffset, inputCount, outputBuffer, outputOffset);
            }

            public void Dispose()
            {
                _aes.Dispose();
                _encryptor.Dispose();
                _decryptor.Dispose();
            }
        }
    }
}
