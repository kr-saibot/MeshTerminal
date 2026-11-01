using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MeshTerminal.Client;
using MessageBox = MeshTerminal.SafeMessageBox;

namespace MeshTerminal
{
    internal static partial class Program
    {
        private static readonly HashSet<Guid> ResendingMessages = new HashSet<Guid>();
        private static StoredMeshMessage LatestChessMessage(uint partner)
        {
            return _store.GetDirectMessages(partner).LastOrDefault(m => { ChessPosition position; return ChessProtocol.TryParse(m.Text, out position); });
        }
        private static void ResendFailedMessage(StoredMeshMessage message)
        {
            if (!MessageResend.CanRetry(message) || !ResendingMessages.Add(message.Id)) return;
            Task.Run(async () =>
            {
                try
                {
                    await MessageResend.SendAsync(message, _mesh, _store);
                    Ui(() => { RefreshChats(); _refreshChessDialog?.Invoke(); });
                }
                catch (Exception ex) { Ui(() => MessageBox.ErrorQuery("Resend failed", ex.Message, "OK")); }
                finally { Ui(() => ResendingMessages.Remove(message.Id)); }
            });
        }
    }
}
