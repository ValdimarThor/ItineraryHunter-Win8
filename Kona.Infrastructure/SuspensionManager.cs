// THIS CODE AND INFORMATION IS PROVIDED "AS IS" WITHOUT WARRANTY OF
// ANY KIND, EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO
// THE IMPLIED WARRANTIES OF MERCHANTABILITY AND/OR FITNESS FOR A
// PARTICULAR PURPOSE.
//
// Copyright (c) Microsoft Corporation. All rights reserved


using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Kona.Infrastructure.Interfaces;
using Windows.ApplicationModel.Resources;
using Windows.Security.Cryptography.DataProtection;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml;

namespace Kona.Infrastructure
{
    /// <summary>
    /// SuspensionManager captures global session state to simplify process lifetime management
    /// for an application.  Note that session state will be automatically cleared under a variety
    /// of conditions and should only be used to store information that would be convenient to
    /// carry across sessions, but that should be discarded when an application crashes or is
    /// upgraded.
    /// </summary>
    public sealed class SuspensionManager
    {
        private static Dictionary<string, object> _sessionState = new Dictionary<string, object>();
        private static List<Type> _knownTypes = new List<Type>();

        private SuspensionManager()
        {
        }

        /// <summary>
        /// Provides access to global session state for the current session.  This state is
        /// serialized by <see cref="SaveAsync"/> and restored by
        /// <see cref="RestoreAsync"/>, so values must be serializable by
        /// <see cref="DataContractSerializer"/> and should be as compact as possible.  Strings
        /// and other self-contained data types are strongly recommended.
        /// </summary>
        public static Dictionary<string, object> SessionState
        {
            get { return _sessionState; }
        }

        /// <summary>
        /// List of custom types provided to the <see cref="DataContractSerializer"/> when
        /// reading and writing session state.  Initially empty, additional types may be
        /// added to customize the serialization process.
        /// </summary>
        public static List<Type> KnownTypes
        {
            get { return _knownTypes; }
        }

        /// <summary>
        /// Save the current <see cref="SessionState"/>.  Any <see cref="Frame"/> instances
        /// registered with <see cref="RegisterFrame"/> will also preserve their current
        /// navigation stack, which in turn gives their active <see cref="Page"/> an opportunity
        /// to save its state.
        /// </summary>
        /// <returns>An asynchronous task that reflects when session state has been saved.</returns>
        public static async Task SaveAsync()
        {
            try
            {
            // Save the navigation state for all registered frames
            foreach (var weakFrameReference in _registeredFrames)
            {
                IFrameFacade frame;
                if (weakFrameReference.TryGetTarget(out frame))
                {
                    SaveFrameNavigationState(frame);
                }
            }

            // Serialize the session state synchronously to avoid asynchronous access to shared
            // state
            MemoryStream sessionData = new MemoryStream();
            DataContractSerializer serializer = new DataContractSerializer(typeof(Dictionary<string, object>), _knownTypes);
            serializer.WriteObject(sessionData, _sessionState);
            
                // Get an output stream for the SessionState file and write the state asynchronously
                StorageFile file = await ApplicationData.Current.LocalFolder.CreateFileAsync(Constants.SessionStateFileName, CreationCollisionOption.ReplaceExisting);
                using (var fileStream = await file.OpenAsync(FileAccessMode.ReadWrite)) 
                {
                    sessionData.Seek(0, SeekOrigin.Begin);
                    var provider = new DataProtectionProvider("LOCAL=user");

                    // Encrypt the session data and write it to disk.
                    await provider.ProtectStreamAsync(sessionData.AsInputStream(), fileStream);
                    await fileStream.FlushAsync();
                }
            }
            catch (Exception e)
            {
                throw new SuspensionManagerException(e);
            }
        }

        /// <summary>
        /// Restores previously saved <see cref="SessionState"/>.
        /// </summary>
        /// <returns>An asynchronous task that reflects when session state has been read.  The
        /// content of <see cref="SessionState"/> should not be relied upon until this task
        /// completes.</returns>
        public async static Task RestoreSessionStateAsync()
        {
            _sessionState = new Dictionary<String, Object>();

            try
            {
                // Get the input stream for the SessionState file
                StorageFile file = await ApplicationData.Current.LocalFolder.GetFileAsync(Constants.SessionStateFileName);
                using (IInputStream inStream = await file.OpenSequentialReadAsync())
                {
                    var memoryStream = new MemoryStream();
                    var provider = new DataProtectionProvider("LOCAL=user");

                    // Decrypt the prevously saved session data.
                    await provider.UnprotectStreamAsync(inStream, memoryStream.AsOutputStream());
                    memoryStream.Seek(0, SeekOrigin.Begin);
                    // Deserialize the Session State
                    DataContractSerializer serializer = new DataContractSerializer(typeof(Dictionary<string, object>),
                                                                                   _knownTypes);
                    _sessionState = (Dictionary<string, object>)serializer.ReadObject(memoryStream);
                }
            }
            catch (Exception e)
            {
                throw new SuspensionManagerException(e);
            }
        }


        /// <summary>
        /// Any <see cref="Frame"/> instances registered with <see cref="RegisterFrame"/> will 
        /// restore their prior navigation state, which in turn gives their active <see cref="Page"/> 
        /// an opportunity restore its state.
        /// 
        /// This method requires that RestoreSessionStateAsync be call prior to this method.
        /// </summary>
        public static void RestoreFrameState()
        {
            try
            {
                // Restore any registered frames to their saved state
                foreach (var weakFrameReference in _registeredFrames)
                {
                    IFrameFacade frame;
                    if (weakFrameReference.TryGetTarget(out frame))
                    {
                        frame.ClearValue(FrameSessionStateProperty);
                        RestoreFrameNavigationState(frame);
                    }
                }
            }
            catch (Exception e)
            {
                throw new SuspensionManagerException(e);
            }
        }

        private static DependencyProperty FrameSessionStateKeyProperty =
            DependencyProperty.RegisterAttached("_FrameSessionStateKey", typeof(String), typeof(SuspensionManager), null);
        private static DependencyProperty FrameSessionStateProperty =
            DependencyProperty.RegisterAttached("_FrameSessionState", typeof(Dictionary<String, Object>), typeof(SuspensionManager), null);
        private static List<WeakReference<IFrameFacade>> _registeredFrames = new List<WeakReference<IFrameFacade>>();

        /// <summary>
        /// Registers a <see cref="Frame"/> instance to allow its navigation history to be saved to
        /// and restored from <see cref="SessionState"/>.  Frames should be registered once
        /// immediately after creation if they will participate in session state management.  Upon
        /// registration if state has already been restored for the specified key
        /// the navigation history will immediately be restored.  Subsequent invocations of
        /// <see cref="RestoreAsync"/> will also restore navigation history.
        /// </summary>
        /// <param name="frame">An instance whose navigation history should be managed by
        /// <see cref="SuspensionManager"/></param>
        /// <param name="sessionStateKey">A unique key into <see cref="SessionState"/> used to
        /// store navigation-related information.</param>
        public static void RegisterFrame(IFrameFacade frame, String sessionStateKey)
        {
            var resourceLoader = new ResourceLoader(Constants.KonaInfrastructureResourceMapId);

            if (frame.GetValue(FrameSessionStateKeyProperty) != null)
            {
                var errorString = resourceLoader.GetString("FrameAlreadyRegisteredWithKey");
                throw new InvalidOperationException(errorString);
            }

            if (frame.GetValue(FrameSessionStateProperty) != null)
            {
                var errorString = resourceLoader.GetString("FrameRegistrationRequirement");
                throw new InvalidOperationException(errorString);
            }

            // Use a dependency property to associate the session key with a frame, and keep a list of frames whose
            // navigation state should be managed
            frame.SetValue(FrameSessionStateKeyProperty, sessionStateKey);
            _registeredFrames.Add(new WeakReference<IFrameFacade>(frame));

            // Check to see if navigation state can be restored
            RestoreFrameNavigationState(frame);
        }

        /// <summary>
        /// Disassociates a <see cref="Frame"/> previously registered by <see cref="RegisterFrame"/>
        /// from <see cref="SessionState"/>.  Any navigation state previously captured will be
        /// removed.
        /// </summary>
        /// <param name="frame">An instance whose navigation history should no longer be
        /// managed.</param>
        public static void UnregisterFrame(IFrameFacade frame)
        {
            // Remove session state and remove the frame from the list of frames whose navigation
            // state will be saved (along with any weak references that are no longer reachable)
            SessionState.Remove((String)frame.GetValue(FrameSessionStateKeyProperty));
            _registeredFrames.RemoveAll((weakFrameReference) =>
            {
                IFrameFacade testFrame;
                return !weakFrameReference.TryGetTarget(out testFrame) || testFrame == frame;
            });
        }

        /// <summary>
        /// Provides storage for session state associated with the specified <see cref="Frame"/>.
        /// Frames that have been previously registered with <see cref="RegisterFrame"/> have
        /// their session state saved and restored automatically as a part of the global
        /// <see cref="SessionState"/>.  Frames that are not registered have transient state
        /// that can still be useful when restoring pages that have been discarded from the
        /// navigation cache.
        /// </summary>
        /// <remarks>Apps may choose to rely on <see cref="LayoutAwarePage"/> to manage
        /// page-specific state instead of working with frame session state directly.</remarks>
        /// <param name="frame">The instance for which session state is desired.</param>
        /// <returns>A collection of state subject to the same serialization mechanism as
        /// <see cref="SessionState"/>.</returns>
        public static Dictionary<String, Object> SessionStateForFrame(IFrameFacade frame)
        {
            var frameState = (Dictionary<String, Object>)frame.GetValue(FrameSessionStateProperty);

            if (frameState == null)
            {
                var frameSessionKey = (String)frame.GetValue(FrameSessionStateKeyProperty);
                if (frameSessionKey != null)
                {
                    // Registered frames reflect the corresponding session state
                    if (!_sessionState.ContainsKey(frameSessionKey))
                    {
                        _sessionState[frameSessionKey] = new Dictionary<String, Object>();
                    }
                    frameState = (Dictionary<String, Object>)_sessionState[frameSessionKey];
                }
                else
                {
                    // Frames that aren't registered have transient state
                    frameState = new Dictionary<String, Object>();
                }
                frame.SetValue(FrameSessionStateProperty, frameState);
            }
            return frameState;
        }

        private static void RestoreFrameNavigationState(IFrameFacade frame)
        {
            var frameState = SessionStateForFrame(frame);
            if (frameState.ContainsKey("Navigation"))
            {
                frame.SetNavigationState((String)frameState["Navigation"]);
            }
        }

        private static void SaveFrameNavigationState(IFrameFacade frame)
        {
            var frameState = SessionStateForFrame(frame);
            frameState["Navigation"] = frame.GetNavigationState();
        }
    }
    public class SuspensionManagerException : Exception
    {
        public SuspensionManagerException()
        {
        }

        public SuspensionManagerException(Exception e) : base("SuspensionManager failed", e)
        {
            
        }
    }
}
