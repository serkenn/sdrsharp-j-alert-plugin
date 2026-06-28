// Minimal reference ("stub") declarations of the SDRSharp.Radio public API used
// by the J-ALERT plugin. See ../README.md. Verified against the SDR# source
// mirror SDRSharpR/SDRSharp. NOT a real implementation — the host provides the
// real assembly at run time.

namespace SDRSharp.Radio
{
    public struct Complex
    {
        public float Real;
        public float Imag;
        public Complex(float real, float imaginary) { Real = real; Imag = imaginary; }
    }

    public enum ProcessorType
    {
        RawIQ,
        DecimatedAndFilteredIQ,
        DemodulatorOutput,
        FilteredAudioOutput,
        FMMPX,
        RDSBitStream
    }

    public interface IBaseProcessor
    {
        bool Enabled { get; set; }
    }

    public interface IStreamProcessor : IBaseProcessor
    {
        double SampleRate { set; }
    }

    public unsafe interface IIQProcessor : IStreamProcessor, IBaseProcessor
    {
        void Process(Complex* buffer, int length);
    }

    public unsafe interface IRealProcessor : IStreamProcessor, IBaseProcessor
    {
        void Process(float* buffer, int length);
    }
}
