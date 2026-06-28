// Minimal reference ("stub") declarations of the SDRSharp.Common public API used
// by the J-ALERT plugin. See ../README.md. Verified against the SDR# source
// mirror SDRSharpR/SDRSharp. NOT a real implementation — the host provides the
// real assembly at run time.

using System.Windows.Forms;
using SDRSharp.Radio;

namespace SDRSharp.Common
{
    public interface ISharpPlugin
    {
        UserControl Gui { get; }
        string DisplayName { get; }
        void Initialize(ISharpControl control);
        void Close();
    }

    // Only the members the plugin actually calls are declared. A reference
    // assembly may be a subset of the real interface for code that consumes
    // (does not implement) it.
    public interface ISharpControl
    {
        long Frequency { get; set; }
        long CenterFrequency { get; set; }
        int RFBandwidth { get; }
        bool IsPlaying { get; }
        void StartRadio();
        void StopRadio();
        void RegisterStreamHook(object streamHook, ProcessorType processorType);
        void UnregisterStreamHook(object streamHook);
    }
}
