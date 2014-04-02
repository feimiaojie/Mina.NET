﻿using System;
using System.Net;
using System.Net.Sockets;
using Mina.Core.Buffer;
using Mina.Core.File;
using Mina.Core.Service;
using Mina.Core.Write;
using Mina.Util;

namespace Mina.Transport.Socket
{
    public class AsyncSocketSession : SocketSession
    {
        public static readonly ITransportMetadata Metadata
            = new DefaultTransportMetadata("async", "socket", false, true, typeof(IPEndPoint));

        private readonly SocketAsyncEventArgsBuffer _readBuffer;
        private readonly SocketAsyncEventArgsBuffer _writeBuffer;
        private readonly EventHandler<SocketAsyncEventArgs> _completeHandler;

        public AsyncSocketSession(IoService service, IoProcessor<SocketSession> processor, System.Net.Sockets.Socket socket,
            SocketAsyncEventArgsBuffer readBuffer, SocketAsyncEventArgsBuffer writeBuffer, Boolean reuseBuffer)
            : base(service, processor, socket)
        {
            _readBuffer = readBuffer;
            _readBuffer.SocketAsyncEventArgs.UserToken = this;
            _writeBuffer = writeBuffer;
            _writeBuffer.SocketAsyncEventArgs.UserToken = this;
            _completeHandler = saea_Completed;
            ReuseBuffer = reuseBuffer;
        }

        /// <summary>
        /// Gets the reading buffer belonged to this session.
        /// </summary>
        public SocketAsyncEventArgsBuffer ReadBuffer
        {
            get { return _readBuffer; }
        }

        /// <summary>
        /// Gets the writing buffer belonged to this session.
        /// </summary>
        public SocketAsyncEventArgsBuffer WriteBuffer
        {
            get { return _writeBuffer; }
        }

        /// <inheritdoc/>
        public override ITransportMetadata TransportMetadata
        {
            get { return Metadata; }
        }

        /// <inheritdoc/>
        protected override void BeginSend(IWriteRequest request, IoBuffer buf)
        {
            BeginSend(buf);
        }

        /// <inheritdoc/>
        protected override void BeginSendFile(IWriteRequest request, IFileRegion file)
        {
            BeginSendFile(file);
        }

        private void BeginSend(IoBuffer buf)
        {
            _writeBuffer.Clear();

            SocketAsyncEventArgs saea;
            SocketAsyncEventArgsBuffer saeaBuf = buf as SocketAsyncEventArgsBuffer;
            if (saeaBuf == null)
            {
                if (_writeBuffer.Remaining < buf.Remaining)
                {
                    Int32 oldLimit = buf.Limit;
                    buf.Limit = buf.Position + _writeBuffer.Remaining;
                    _writeBuffer.Put(buf);
                    buf.Limit = oldLimit;
                }
                else
                {
                    _writeBuffer.Put(buf);
                }
                _writeBuffer.Flip();
                saea = _writeBuffer.SocketAsyncEventArgs;
                saea.SetBuffer(saea.Offset + _writeBuffer.Position, _writeBuffer.Limit);
            }
            else
            {
                saea = saeaBuf.SocketAsyncEventArgs;
                saea.Completed += _completeHandler;
            }

            try
            {
                Boolean willRaiseEvent = Socket.SendAsync(saea);
                if (!willRaiseEvent)
                {
                    ProcessSend(saea);
                }
            }
            catch (Exception ex)
            {
                ExceptionMonitor.Instance.ExceptionCaught(ex);
            }
        }

        private void BeginSendFile(Core.File.IFileRegion file)
        {
            SocketAsyncEventArgs saea = _writeBuffer.SocketAsyncEventArgs;
            saea.SendPacketsElements = new SendPacketsElement[] {
                new SendPacketsElement(file.FullName)
            };

            try
            {
                Boolean willRaiseEvent = Socket.SendPacketsAsync(saea);
                if (!willRaiseEvent)
                {
                    ProcessSend(saea);
                }
            }
            catch (Exception ex)
            {
                ExceptionMonitor.Instance.ExceptionCaught(ex);
            }
        }

        void saea_Completed(object sender, SocketAsyncEventArgs e)
        {
            e.Completed -= _completeHandler;
            ProcessSend(e);
        }

        /// <summary>
        /// Processes send events.
        /// </summary>
        /// <param name="e"></param>
        public void ProcessSend(SocketAsyncEventArgs e)
        {
            if (e.SocketError == SocketError.Success)
            {
                EndSend(e.BytesTransferred);
                // TODO e.BytesTransferred == 0
            }
            else
            {
                ExceptionMonitor.Instance.ExceptionCaught(new SocketException((Int32)e.SocketError));

                // closed
                Processor.Remove(this);
            }
        }

        /// <inheritdoc/>
        protected override void BeginReceive()
        {
            _readBuffer.Clear();
            try
            {
                Boolean willRaiseEvent = Socket.ReceiveAsync(_readBuffer.SocketAsyncEventArgs);
                if (!willRaiseEvent)
                {
                    ProcessReceive(_readBuffer.SocketAsyncEventArgs);
                }
            }
            catch (Exception ex)
            {
                ExceptionMonitor.Instance.ExceptionCaught(ex);
            }
        }

        /// <summary>
        /// Processes receive events.
        /// </summary>
        /// <param name="e"></param>
        public void ProcessReceive(SocketAsyncEventArgs e)
        {
            if (e.SocketError == SocketError.Success)
            {
                if (e.BytesTransferred > 0)
                {
                    _readBuffer.Position = e.BytesTransferred;
                    _readBuffer.Flip();

                    if (ReuseBuffer)
                    {
                        EndReceive(_readBuffer);
                    }
                    else
                    {
                        IoBuffer buf = IoBuffer.Allocate(_readBuffer.Remaining);
                        buf.Put(_readBuffer);
                        buf.Flip();
                        EndReceive(buf);
                    }

                    return;
                }
            }
            else
            {
                ExceptionMonitor.Instance.ExceptionCaught(new SocketException((Int32)e.SocketError));
            }

            // closed
            Processor.Remove(this);
        }
    }
}
