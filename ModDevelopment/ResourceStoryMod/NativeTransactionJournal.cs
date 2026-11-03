using System;
using System.IO;
using System.Text.RegularExpressions;

namespace ResourceStoryMod
{
    // A pending marker means the whole native save must roll back before the game's loader reads it.
    // Crash after native SaveGame but before Commit is allowed to undo that last interaction.
    internal sealed class NativeTransactionJournal
    {
        private readonly string target,backup,marker;
        internal NativeTransactionJournal(string path)
        {
            target=Path.GetFullPath(path);backup=target+".millsage-before";marker=target+".millsage-pending";
            if(!Regex.IsMatch(Path.GetFileName(target),@"^save_\d+\.es3$"))throw new InvalidDataException("Unexpected native save slot");
            if(File.Exists(marker))throw new InvalidOperationException("An interrupted transaction needs recovery before continuing");
            if(!File.Exists(target))throw new FileNotFoundException("Native save must exist before a transaction",target);
            File.Copy(target,backup,true);
            // The backup is durable before publishing the marker. Never put a caller-supplied restore path in it.
            File.WriteAllText(marker,"millsage-native-transaction-v1");
        }
        internal void Commit(){
            File.Delete(marker);
            // Removing the marker commits. A cleanup failure must not undo only the in-memory half.
            try{File.Delete(backup);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
        internal void Rollback(){if(File.Exists(marker)){File.Copy(backup,target,true);Commit();}}
        internal static int Recover(string directory)
        {
            if(!System.IO.Directory.Exists(directory))return 0;
            int count=0;
            foreach(var marker in System.IO.Directory.EnumerateFiles(directory,"save_*.es3.millsage-pending",SearchOption.TopDirectoryOnly)) {
                string target=marker.Substring(0,marker.Length-".millsage-pending".Length);
                if(!Regex.IsMatch(Path.GetFileName(target),@"^save_\d+\.es3$") || File.ReadAllText(marker)!="millsage-native-transaction-v1" || !File.Exists(target+".millsage-before"))throw new InvalidDataException("Transaction recovery data is incomplete; original files retained");
                File.Copy(target+".millsage-before",target,true);File.Delete(marker);File.Delete(target+".millsage-before");count++;
            }
            return count;
        }
    }
}
