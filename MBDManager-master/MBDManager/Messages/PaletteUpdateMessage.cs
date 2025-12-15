using CommunityToolkit.Mvvm.Messaging.Messages;

namespace MBDManager.Messages
{
    public class PaletteUpdateMessage : ValueChangedMessage<(string Zone, int Count)>
    {
        public PaletteUpdateMessage((string Zone, int Count) value) : base(value) { }
    }
}