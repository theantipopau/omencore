using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using OmenCore.Corsair;

namespace OmenCore.Services
{
    public class MacroService
    {
        private readonly ObservableCollection<MacroAction> _buffer = new();
        private bool _recording;

        public ReadOnlyObservableCollection<MacroAction> Buffer { get; }

        public MacroService()
        {
            Buffer = new ReadOnlyObservableCollection<MacroAction>(_buffer);
        }

        public void StartRecording()
        {
            _buffer.Clear();
            _recording = true;
        }

        public void StopRecording()
        {
            _recording = false;
        }

        public void PushEvent(Key key, bool down, int delayMs)
        {
            if (!_recording)
            {
                return;
            }
            _buffer.Add(new MacroAction { Key = key, KeyDown = down, DelayMs = delayMs });
        }

        public MacroProfile BuildProfile(string name) => new()
        {
            Name = name,
            Actions = new List<MacroAction>(_buffer)
        };
    }
}
