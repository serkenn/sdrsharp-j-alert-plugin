// SDR# plugin entry point. Registers an IQ stream hook on the
// DecimatedAndFilteredIQ stream and exposes the side panel. This is the SDR#
// analogue of the SDR++ ModuleManager::Instance in the original main.cpp.

using System.Windows.Forms;
using SDRSharp.Common;
using SDRSharp.Radio;

namespace SDRSharp.JAlert
{
    public sealed class JAlertPlugin : ISharpPlugin
    {
        private ISharpControl _control;
        private JAlertSettings _settings;
        private JAlertProcessor _processor;
        private JAlertPanel _panel;

        public string DisplayName => "J-ALERT Decoder";

        public UserControl Gui => _panel;

        public void Initialize(ISharpControl control)
        {
            _control = control;
            _settings = JAlertSettings.Load();
            _processor = new JAlertProcessor(control, _settings);
            _panel = new JAlertPanel(_processor, _settings);

            // Tap the decimated, filtered IQ (centered on the spectrum center
            // frequency); the processor NCO-shifts the tuned carrier to baseband.
            control.RegisterStreamHook(_processor, ProcessorType.DecimatedAndFilteredIQ);
        }

        public void Close()
        {
            if (_control != null && _processor != null)
                _control.UnregisterStreamHook(_processor);
            _processor?.Close();
            _settings?.Save();
        }
    }
}
