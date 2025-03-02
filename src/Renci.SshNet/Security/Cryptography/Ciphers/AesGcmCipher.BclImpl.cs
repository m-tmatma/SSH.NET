#if NET
using System;
using System.Security.Cryptography;

using Renci.SshNet.Common;
using Renci.SshNet.Messages.Transport;

namespace Renci.SshNet.Security.Cryptography.Ciphers
{
    internal partial class AesGcmCipher
    {
        private sealed class BclImpl : Impl
        {
            private readonly AesGcm _aesGcm;
            private readonly byte[] _nonce;

            public BclImpl(byte[] key, byte[] nonce)
            {
                _aesGcm = new AesGcm(key, TagSizeInBytes);
                _nonce = nonce;
            }

            public override void Encrypt(byte[] input, int plainTextOffset, int plainTextLength, int associatedDataOffset, int associatedDataLength, byte[] output, int cipherTextOffset)
            {
                var cipherTextLength = plainTextLength;
                var plainText = new ReadOnlySpan<byte>(input, plainTextOffset, plainTextLength);
                var cipherText = new Span<byte>(output, cipherTextOffset, cipherTextLength);
                var tag = new Span<byte>(output, cipherTextOffset + cipherTextLength, TagSizeInBytes);
                var associatedData = new ReadOnlySpan<byte>(input, associatedDataOffset, associatedDataLength);

                _aesGcm.Encrypt(_nonce, plainText, cipherText, tag, associatedData);
            }

            public override void Decrypt(byte[] input, int cipherTextOffset, int cipherTextLength, int associatedDataOffset, int associatedDataLength, byte[] output, int plainTextOffset)
            {
                var cipherText = new ReadOnlySpan<byte>(input, cipherTextOffset, cipherTextLength);
                var tag = new ReadOnlySpan<byte>(input, cipherTextOffset + cipherTextLength, TagSizeInBytes);
                var associatedData = new ReadOnlySpan<byte>(input, associatedDataOffset, associatedDataLength);

                try
                {
                    _aesGcm.Decrypt(_nonce, cipherText, tag, output.AsSpan(plainTextOffset), associatedData);
                }
                catch (AuthenticationTagMismatchException ex)
                {
                    throw new SshConnectionException("MAC error", DisconnectReason.MacError, ex);
                }
            }

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);

                if (disposing)
                {
                    _aesGcm.Dispose();
                }
            }
        }
    }
}
#endif
