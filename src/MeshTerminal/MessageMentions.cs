using System;
using System.Collections.Generic;
using System.Linq;
using MeshTerminal.Client;
namespace MeshTerminal
{
    internal static class MessageMentions
    {
        // Input is in database reception/insertion order, not the sender's potentially wrong clock.
        internal static List<string> RecentNames(IEnumerable<StoredMeshMessage> messages,Func<uint,string> contactName,uint? ownNode)
        {
            var result=new List<string>();var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(var message in messages.Reverse())
            {
                if(message.Direction!=MessageDirection.Incoming||(ownNode.HasValue&&message.FromNode==ownNode))continue;
                var name=message.SenderName;
                if(String.IsNullOrWhiteSpace(name)&&message.FromNode.HasValue)name=contactName(message.FromNode.Value);
                if(String.IsNullOrWhiteSpace(name))continue;
                name=new string(name.Select(c=>Char.IsControl(c)?' ':c).ToArray()).Trim();
                if(name.Length>0&&seen.Add(name))result.Add(name);
            }
            return result;
        }
    }
}
