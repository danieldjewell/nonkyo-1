﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Net.Sockets;

namespace NOnkyo.ISCP
{
    public class MessageReceivedEventArgs : EventArgs
    {
        public MessageReceivedEventArgs(string psMessage)
        {
            this.Message = psMessage;
        }
        public string Message { get; private set; }
    }

    public class Connection : IDisposable, IConnection
    {
        #region Logging

        private static Lazy<NLog.Logger> moLazyLogger = new Lazy<NLog.Logger>(() => NLog.LogManager.GetCurrentClassLogger());

        private static NLog.Logger Logger
        {
            get
            {
                return moLazyLogger.Value;
            }
        }

        #endregion

        #region Event MessageReceived

        [NonSerialized()]
        private EventHandler<MessageReceivedEventArgs> EventMessageReceived;
        public event EventHandler<MessageReceivedEventArgs> MessageReceived
        {
            add
            { this.EventMessageReceived += value; }
            remove
            { this.EventMessageReceived -= value; }
        }

        protected void OnMessageReceived(string psMessage)
        {
            EventHandler<MessageReceivedEventArgs> loHandler = this.EventMessageReceived;
            if (loHandler != null)
                loHandler(this, new MessageReceivedEventArgs(psMessage));
        }

        #endregion Event PropertyChanged

        #region Event ConnectionClosed
        
        [NonSerialized()]
        private EventHandler EventConnectionClosed;
        public event EventHandler ConnectionClosed
        {
            add
            { this.EventConnectionClosed += value; } 
            remove
            { this.EventConnectionClosed -= value; } 
        }

        protected virtual void OnConnectionClosed()
        {
            EventHandler loHandler = this.EventConnectionClosed;
            if (loHandler != null)
                loHandler(this, EventArgs.Empty);
        }

        #endregion

        #region Attributes

        private Socket moSocket = null;
       
        #endregion

        #region Public Methods / Properties

        public Device CurrentDevice { get; private set; }

        public bool Connect(Device poDevice)
        {
            bool lbSuccess = false;

            this.CurrentDevice = poDevice;
            this.Disconnect();
            this.moSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
            {

                ReceiveTimeout = 2000,
            };

            var loAsyncResult = this.moSocket.BeginConnect(this.CurrentDevice.IP, this.CurrentDevice.Port, null, null);
            loAsyncResult.AsyncWaitHandle.WaitOne(2000, true);
            lbSuccess = this.moSocket.Connected;

            if (lbSuccess)
                ThreadPool.QueueUserWorkItem(new WaitCallback(this.SocketListener));
            else
                this.Disconnect();

            return lbSuccess;
        }

        public void Disconnect()
        {
            if (this.moSocket != null)
            {
                try
                {
                    this.moSocket.Close(1000);
                }
                catch
                { }
                this.moSocket.Dispose();
                this.moSocket = null;
            }
        }

        public void SendCommand(Command.CommandBase poCommand)
        {
            this.SendPackage(poCommand.CommandMessage);
        }

        public void SendPackage(string psMessage)
        {
            var loPackage = psMessage.ToISCPCommandMessage();
            if (this.moSocket.Connected)
            {
                Logger.Info("Send Message: {0}", psMessage);
                Logger.Debug("Send byte [] {0}{1}", Environment.NewLine, loPackage.FormatToOutput());
                this.moSocket.Send(loPackage, 0, loPackage.Length, SocketFlags.None);
            }
        }

        private void SocketListener(Object stateInfo)
        {
            byte[] loNotProcessingBytes = null;
            byte[] loResultBuffer;
            try
            {
                while (this.moSocket != null)
                {
                    try
                    {
                        if (this.moSocket.Available > 0)
                        {
                            var loBuffer = new byte[1024];
                            this.moSocket.Receive(loBuffer, loBuffer.Length, SocketFlags.None);

                            if (loNotProcessingBytes != null && loNotProcessingBytes.Length > 0)
                                loResultBuffer = loNotProcessingBytes.Concat(loBuffer).ToArray();
                            else
                                loResultBuffer = loBuffer;

                            Logger.Debug("Receive byte [] {0}{1}", Environment.NewLine, loResultBuffer.FormatToOutput());
                            foreach (var lsMessage in loResultBuffer.ToISCPStatusMessage(out loNotProcessingBytes))
                            {
                                Logger.Info("Receive Message {0}", lsMessage);
                                if (lsMessage.IsNotEmpty())
                                    this.OnMessageReceived(lsMessage);
                            }
                        }
                    }
                    catch (Exception exp)
                    {
                        Logger.LogException(NLog.LogLevel.Error, "Unhandled Exception in Messagelistener", exp);
                    }
                    Thread.Sleep(100);
                }
            }
            catch
            { }
            finally
            {
                this.OnConnectionClosed();
            }
        }

        #endregion

        #region IDisposable

        // Track whether Dispose has been called.
        private bool mbDisposed = false;

        /// <summary>
        /// Implement IDisposable
        /// Do not make this method virtual.
        /// A derived class should not be able to override this method.
        /// </summary>
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Dispose(bool disposing) executes in two distinct scenarios.
        /// If disposing equals true, the method has been called directly
        /// or indirectly by a user's code. Managed and unmanaged resources
        /// can be disposed.
        /// If disposing equals false, the method has been called by the 
        /// runtime from inside the finalizer and you should not reference 
        /// other objects. Only unmanaged resources can be disposed.
        /// </summary>
        /// <param name="disposing">true, to dispose managed ad unmanaged resources, false to dispose unmanaged resources only</param>
        protected virtual void Dispose(bool disposing)
        {
            // Note that this is not thread safe.
            // Another thread could start disposing the object
            // after the managed resources are disposed,
            // but before the disposed flag is set to true.
            // If thread safety is necessary, it must be
            // implemented by the client.

            // Check to see if Dispose has already been called.
            if (!this.mbDisposed)
            {
                try
                {
                    // If disposing equals true, dispose all managed 
                    // and unmanaged resources.
                    if (disposing)
                    {
                        // Dispose managed resources. HERE ->
                        this.Disconnect();
                        this.EventMessageReceived = null;

                        //Release ComObjects
                        //System.Runtime.InteropServices.Marshal.ReleaseComObject(ComObj);
                    }
                    // Release unmanaged resources. If disposing is false, 
                    // only the following code is executed. HERE ->
                }
                catch (Exception)
                {
                    this.mbDisposed = false;
                    throw;
                }
                this.mbDisposed = true;
            }
        }

        /// <summary>
        /// Use C# destructor syntax for finalization code.
        /// This destructor will run only if the Dispose method 
        /// does not get called.
        /// It gives your base class the opportunity to finalize.
        /// Do not provide destructors in types derived from this class.
        /// </summary>
        ~Connection()
        {
            // Do not re-create Dispose clean-up code here.
            // Calling Dispose(false) is optimal in terms of
            // readability and maintainability.
            this.Dispose(false);
        }
        #endregion IDisposable

    }
}

