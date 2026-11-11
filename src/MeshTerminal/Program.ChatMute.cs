using System;
using System.Linq;
using MeshTerminal.Client;
using Terminal.Gui;

namespace MeshTerminal
{
    internal sealed class ChatMuteListView : ListView
    {
        public Func<int, bool> IsMuted { get; set; }
        public override void Redraw(Rect bounds)
        {
            base.Redraw(bounds);
            if (Bounds.Width < 1 || Source == null) return;
            for (int row = 0; row < Bounds.Height && TopItem + row < Source.Count; row++)
            {
                if (IsMuted?.Invoke(TopItem + row) != true) continue;
                var selected = HasFocus && SelectedItem == TopItem + row;
                Application.Driver.SetAttribute(selected ? ColorScheme.Focus : ColorScheme.Normal);
                Move(Bounds.Width - 1, row);
                Application.Driver.AddRune('M');
            }
        }
    }

    internal static partial class Program
    {
        private static Button _chatMuteButton;
        private static void UpdateChatMuteButton()
        {
            if (_chatMuteButton == null) return;
            _chatMuteButton.Enabled = _selected != null;
            _chatMuteButton.Text = _selected != null && IsChatMuted(_selected.Kind == ChatKind.Direct, _selected.Id) ? "Unmute" : "Mute";
            _chatMuteButton.SetNeedsDisplay();
        }

        private static bool IsChatMuted(bool direct, uint id)
        {
            return ChatMutePolicy.IsMuted(_settings, _mesh?.Protocol, direct, id);
        }
        private static int CountAlertMessages()
        {
            return ChatMutePolicy.CountUnread(_store, _settings, _mesh?.Protocol);
        }
        private static void ToggleChatMute(ChatItem chat)
        {
            var direct = chat.Kind == ChatKind.Direct;
            var muted = IsChatMuted(direct, chat.Id);
            var next = (_settings.Alerts.MutedChats ?? new System.Collections.Generic.List<MutedChatSettings>()).Where(c => c != null && !(c.Protocol == _mesh.Protocol && c.IsDirect == direct && c.Id == chat.Id)).ToList();
            if (!muted) next.Add(new MutedChatSettings { Protocol = _mesh.Protocol, IsDirect = direct, Id = chat.Id });
            _settings.Alerts.MutedChats = next;
            MeshtasticSettingsStore.Save("meshtastic-settings.xml", _settings);
            _channelList.SetNeedsDisplay();
            _directList.SetNeedsDisplay();
            _lastKnownUnreadCount = CountAlertMessages();
            UpdateLogoBlink();
            UpdateChatMuteButton();
        }
    }
}
