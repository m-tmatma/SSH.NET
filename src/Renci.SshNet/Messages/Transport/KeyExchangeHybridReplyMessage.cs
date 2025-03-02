﻿namespace Renci.SshNet.Messages.Transport
{
    /// <summary>
    /// Represents SSH_MSG_KEX_HYBRID_REPLY message.
    /// </summary>
    public class KeyExchangeHybridReplyMessage : Message
    {
        /// <inheritdoc />
        public override string MessageName
        {
            get
            {
                return "SSH_MSG_KEX_HYBRID_REPLY";
            }
        }

        /// <inheritdoc />
        public override byte MessageNumber
        {
            get
            {
                return 31;
            }
        }

        /// <summary>
        /// Gets a string encoding an X.509v3 certificate containing the server's ECDSA public host key.
        /// </summary>
        /// <value>The host key.</value>
        public byte[] KS { get; private set; }

        /// <summary>
        /// Gets the server reply.
        /// </summary>
        /// <remarks>
        /// The server reply is the concatenation of S_CT2 and S_PK1 (S_REPLY = S_CT2 || S_PK1).
        /// Typically, S_PK1 represents the ephemeral (EC)DH server public key.
        /// S_CT2 represents the ciphertext 'ct' output of the corresponding KEM's 'Encaps' algorithm generated by
        /// the server which encapsulates a secret to the client public key C_PK2.
        /// </remarks>
        public byte[] SReply { get; private set; }

        /// <summary>
        /// Gets an octet string containing the server's signature of the newly established exchange hash value.
        /// </summary>
        /// <value>The signature.</value>
        public byte[] Signature { get; private set; }

        /// <summary>
        /// Gets the size of the message in bytes.
        /// </summary>
        /// <value>
        /// The size of the messages in bytes.
        /// </value>
        protected override int BufferCapacity
        {
            get
            {
                var capacity = base.BufferCapacity;
                capacity += 4; // KS length
                capacity += KS.Length; // KS
                capacity += 4; // SReply length
                capacity += SReply.Length; // SReply
                capacity += 4; // Signature length
                capacity += Signature.Length; // Signature
                return capacity;
            }
        }

        /// <summary>
        /// Called when type specific data need to be loaded.
        /// </summary>
        protected override void LoadData()
        {
            KS = ReadBinary();
            SReply = ReadBinary();
            Signature = ReadBinary();
        }

        /// <summary>
        /// Called when type specific data need to be saved.
        /// </summary>
        protected override void SaveData()
        {
            WriteBinaryString(KS);
            WriteBinaryString(SReply);
            WriteBinaryString(Signature);
        }

        internal override void Process(Session session)
        {
            session.OnKeyExchangeHybridReplyMessageReceived(this);
        }
    }
}
