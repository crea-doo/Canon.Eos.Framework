﻿using System;
using System.Threading;
using Canon.Eos.Framework.Eventing;
using Canon.Eos.Framework.Helper;
using Canon.Eos.Framework.Internal;
using Canon.Eos.Framework.Internal.SDK;
using Canon.Eos.Framework.Threading;

namespace Canon.Eos.Framework
{
    partial class EosCamera
    {        
        private bool _liveMode;

        private void OnLiveViewStarted(EventArgs eventArgs)
        {
            if (this.LiveViewStarted != null)
                this.LiveViewStarted(this, eventArgs);
        }

        private void OnLiveViewStopped(EventArgs eventArgs)
        {
            if (this.LiveViewStopped != null)
                this.LiveViewStopped(this, eventArgs);
        }

        private void OnLiveViewUpdate(EosLiveImageEventArgs eventArgs)
        {
            if (this.LiveViewUpdate != null)
                this.LiveViewUpdate(this, eventArgs);            
        }

        private string LiveViewMutexName
        {
            get
            {
                return liveViewMutexNamePrefix + "_" + this.Handle.ToString();
            }
        }

        private bool DownloadEvf()
        {
            if (!this.IsInHostLiveViewMode)
                return false;

            Boolean result = false;

            Mutex liveViewMutex = new Mutex(false, this.LiveViewMutexName);
            if (liveViewMutex.WaitOne(EosCamera.WaitTimeoutForNextLiveDownload, true))
            {
                var memoryStream = IntPtr.Zero;
                try
                {
                    Util.Assert(Edsdk.EdsCreateMemoryStream(0, out memoryStream), "Failed to create memory stream.");
                    using (var image = EosLiveImage.CreateFromStream(memoryStream))
                    {
                        Util.Assert(Edsdk.EdsDownloadEvfImageCdecl(this.Handle, image.Handle), "Failed to download evf image.");

                        var converter = new EosConverter();
                        this.OnLiveViewUpdate(new EosLiveImageEventArgs(converter.ConvertImageStreamToBytes(memoryStream))
                        {
                            Zoom = image.Zoom,
                            ZommBounds = image.ZoomBounds,
                            ImagePosition = image.ImagePosition,
                            Histogram = image.Histogram,
                        });
                    }
                }
                catch (EosException eosEx)
                {
                    if (eosEx.EosErrorCode != EosErrorCode.DeviceBusy && eosEx.EosErrorCode != EosErrorCode.ObjectNotReady)
                        throw;
                }
                finally
                {
                    if (memoryStream != IntPtr.Zero)
                        Edsdk.EdsRelease(memoryStream);
                }

                liveViewMutex.ReleaseMutex();
				result = true;
            }

            liveViewMutex.Dispose();
            return result;
        }

        private void StartDownloadEvfInBackGround()
        {
            var backgroundWorker = new BackgroundWorker();
            backgroundWorker.Work(() =>
            {
                while (this.DownloadEvf())
                    Thread.Sleep(EosCamera.WaitTimeoutForNextLiveDownload);
            });
        }

        private void OnPropertyEventPropertyEvfOutputDeviceChanged(uint param, IntPtr context)
        {            
            if (!_liveMode && (this.LiveViewDevice & EosLiveViewDevice.Host) != EosLiveViewDevice.None)
            {
                _liveMode = true;
                this.OnLiveViewStarted(EventArgs.Empty);
                this.StartDownloadEvfInBackGround();
            }
            else if (_liveMode && (this.LiveViewDevice & EosLiveViewDevice.Host) == EosLiveViewDevice.None)
            {
                _liveMode = false;
                this.OnLiveViewStopped(EventArgs.Empty);
            }
        }

        private void OnPropertyEventPropertyChanged(uint propertyId, uint param, IntPtr context)
        {
            EosFramework.LogInstance.Debug("OnPropertyEventPropertyChanged: " + propertyId);
            switch (propertyId)
            {
                case Edsdk.PropID_Evf_OutputDevice:
                    this.OnPropertyEventPropertyEvfOutputDeviceChanged(param, context);
                    break;
            }
        }

        private void OnPropertyEventPropertyDescChanged(uint propertyId, uint param, IntPtr context)
        {
            EosFramework.LogInstance.Debug("OnPropertyEventPropertyDescChanged: " + propertyId);            
        }

        private uint HandlePropertyEvent(uint propertyEvent, uint propertyId, uint param, IntPtr context)
        {
            EosFramework.LogInstance.Debug("HandlePropertyEvent fired: " + propertyEvent + ", id: " + propertyId);
            switch (propertyEvent)
            {
                case Edsdk.PropertyEvent_PropertyChanged:
                    this.OnPropertyEventPropertyChanged(propertyId, param, context);
                    break;

                case Edsdk.PropertyEvent_PropertyDescChanged:
                    this.OnPropertyEventPropertyDescChanged(propertyId, param, context);
                    break;
            }
            return Edsdk.EDS_ERR_OK;
        }        
    }
}
