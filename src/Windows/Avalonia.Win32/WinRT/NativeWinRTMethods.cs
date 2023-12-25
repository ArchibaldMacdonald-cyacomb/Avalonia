using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Win32.Interop;
using MicroCom.Runtime;

namespace Avalonia.Win32.WinRT
{
    internal static class NativeWinRTMethods
    {
        [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", CallingConvention = CallingConvention.StdCall,
            PreserveSig = false)]
        internal static extern unsafe IntPtr WindowsCreateString(
            [MarshalAs(UnmanagedType.LPWStr)] string sourceString,
            int length);

        internal static IntPtr WindowsCreateString(string sourceString) 
            => WindowsCreateString(sourceString, sourceString.Length);

        [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern unsafe char* WindowsGetStringRawBuffer(IntPtr hstring, uint* length);

        [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", 
            CallingConvention = CallingConvention.StdCall, PreserveSig = false)]
        internal static extern unsafe void WindowsDeleteString(IntPtr hString);
        
        [DllImport("Windows.UI.Composition", EntryPoint = "DllGetActivationFactory",
            CallingConvention = CallingConvention.StdCall, PreserveSig = false)]
        private extern static IntPtr GetWindowsUICompositionActivationFactory(
            IntPtr activatableClassId);

        internal static IActivationFactory GetWindowsUICompositionActivationFactory(string className)
        {//"Windows.UI.Composition.Compositor"
            var s = WindowsCreateString(className);
            var factory = GetWindowsUICompositionActivationFactory(s);
            return MicroComRuntime.CreateProxyFor<IActivationFactory>(factory, true);
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate int GetActivationFactoryDelegate(IntPtr classId, out IntPtr ppv);

        private static int TryCreateInstanceImpl<T>(string fullName, out T? instance)
            where T : IUnknown
        {
            var s = WindowsCreateString(fullName);
            EnsureRoInitialized();
            var hr = RoActivateInstance(s, out var pUnk);
            if (hr == 0)
            {
                using var unk = MicroComRuntime.CreateProxyFor<IUnknown>(pUnk, true);
                WindowsDeleteString(s);
                instance = MicroComRuntime.QueryInterface<T>(unk);
            }
            else
            {
                instance = default;
            }
            return hr;
        }

        internal static bool TryCreateInstance<T>(string fullName, [NotNullWhen(true)] out T? instance)
            where T : IUnknown
        {
            return 0 == TryCreateInstanceImpl(fullName, out instance);
        }

        internal static T CreateInstance<T>(string fullName) where T : IUnknown
        {
            var hr = TryCreateInstanceImpl<T>(fullName, out var instance);
            if (0 != hr)
            {
                Marshal.ThrowExceptionForHR(hr);
            }

            return instance!;
        }
        
        internal static TFactory CreateActivationFactory<TFactory>(string fullName) where TFactory : IUnknown
        {
            var s = WindowsCreateString(fullName);
            EnsureRoInitialized();
            var guid = MicroComRuntime.GetGuidFor(typeof(TFactory));
            var pUnk = RoGetActivationFactory(s, ref guid);
            using var unk = MicroComRuntime.CreateProxyFor<IUnknown>(pUnk, true);
            WindowsDeleteString(s);
            return MicroComRuntime.QueryInterface<TFactory>(unk);
        }
        
        internal enum DISPATCHERQUEUE_THREAD_APARTMENTTYPE
        {
            DQTAT_COM_NONE = 0,
            DQTAT_COM_ASTA = 1,
            DQTAT_COM_STA = 2
        };

        internal enum DISPATCHERQUEUE_THREAD_TYPE
        {
            DQTYPE_THREAD_DEDICATED = 1,
            DQTYPE_THREAD_CURRENT = 2,
        };

        [StructLayout(LayoutKind.Sequential)]
        internal struct DispatcherQueueOptions
        {
            public int dwSize;

            [MarshalAs(UnmanagedType.I4)]
            public DISPATCHERQUEUE_THREAD_TYPE threadType;

            [MarshalAs(UnmanagedType.I4)]
            public DISPATCHERQUEUE_THREAD_APARTMENTTYPE apartmentType;
        };

        [DllImport("coremessaging.dll", PreserveSig = false)]
        internal static extern IntPtr CreateDispatcherQueueController(DispatcherQueueOptions options);

        internal enum RO_INIT_TYPE
        {
            RO_INIT_SINGLETHREADED = 0, // Single-threaded application
            RO_INIT_MULTITHREADED = 1, // COM calls objects on any thread.
        }

        [DllImport("combase.dll", PreserveSig = false)]
        private static extern void RoInitialize(RO_INIT_TYPE initType);

        [DllImport("combase.dll", PreserveSig = true)]
        private static extern int RoActivateInstance(IntPtr activatableClassId, [Out] out IntPtr instance);

        [DllImport("combase.dll", PreserveSig = false)]
        private static extern IntPtr RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid);
        
        private static bool s_initialized;
        private static void EnsureRoInitialized()
        {
            if (s_initialized)
                return;
            RoInitialize(Thread.CurrentThread.GetApartmentState() == ApartmentState.STA ?
                RO_INIT_TYPE.RO_INIT_SINGLETHREADED :
                RO_INIT_TYPE.RO_INIT_MULTITHREADED);
            s_initialized = true;
        }
    }

    internal class HStringInterop : IDisposable
    {
        private IntPtr _s;
        private readonly bool _owns;

        public HStringInterop(string? s)
        {
            _s = s == null ? IntPtr.Zero : NativeWinRTMethods.WindowsCreateString(s);
            _owns = true;
        }
        
        public HStringInterop(IntPtr str, bool owns = false)
        {
            _s = str;
            _owns = owns;
        }

        public IntPtr Handle => _s;

        public unsafe string? Value
        {
            get
            {
                if (_s == IntPtr.Zero)
                    return null;

                uint length;
                var buffer = NativeWinRTMethods.WindowsGetStringRawBuffer(_s, &length);
                return new string(buffer, 0, (int) length);
            }
        }
        
        public void Dispose()
        {
            if (_s != IntPtr.Zero && _owns)
            {
                NativeWinRTMethods.WindowsDeleteString(_s);
                _s = IntPtr.Zero;
            }
        }
    }
}
