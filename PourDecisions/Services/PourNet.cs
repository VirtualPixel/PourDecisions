using Photon.Pun;
using Photon.Realtime;
using PourDecisions.Models;
using PourDecisions.UI;

namespace PourDecisions.Services
{
    // Proper Photon RPCs - no RaiseEvent byte codes. We attach this component to PunManager's
    // GameObject (a persistent singleton whose PhotonView has a stable, shared ViewID that vanilla
    // code already RPCs through), so PUN dispatches our [PunRPC] methods by name with typed args.
    // Every RPC is host-sent and master-gated on receipt, so a client can't spoof drinks.
    public class PourNet : MonoBehaviourPunCallbacks
    {
        public static PourNet? Instance { get; private set; }

        private void Awake() => Instance = this;

        private static PhotonView? View => PunManager.instance != null ? PunManager.instance.photonView : null;
        private static bool CanSend => PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.PlayerCount > 1 && View != null;

        // ---- send (host) ----

        internal static void SendOrder(DrinkOrder order)
        {
            if (!CanSend) return;
            var ids = new string[order.Targets.Count];
            var names = new string[order.Targets.Count];
            int i = 0;
            foreach (var kv in order.Targets) { ids[i] = kv.Key; names[i] = kv.Value; i++; }
            byte flags = (byte)((order.FinishDrink ? 1 : 0) | (order.IsTeam ? 2 : 0));
            View!.RPC(nameof(OrderRPC), RpcTarget.Others, (byte)order.Kind, order.Amount, flags, order.Reason ?? "", ids, names);
        }

        public static void SendDrunkText(string name, string line)
        {
            if (!CanSend) return;
            View!.RPC(nameof(DrunkTextRPC), RpcTarget.Others, name, line);
        }

        public static void SendSnapshot(Player target, string data)
        {
            if (View == null || !PhotonNetwork.InRoom) return;
            View.RPC(nameof(TabSnapshotRPC), target, data);
        }

        // ---- receive (clients) ----

        [PunRPC]
        public void OrderRPC(byte kind, int amount, byte flags, string reason, string[] ids, string[] names, PhotonMessageInfo info = default)
        {
            if (!SemiFunc.MasterOnlyRPC(info)) return;   // only the host hands out drinks
            var order = new DrinkOrder
            {
                Kind = (DrinkEvent)kind,
                Amount = amount,
                FinishDrink = (flags & 1) != 0,
                IsTeam = (flags & 2) != 0,
                Reason = reason ?? "",
            };
            if (ids != null)
                for (int i = 0; i < ids.Length; i++)
                {
                    if (string.IsNullOrEmpty(ids[i])) continue;
                    order.Add(ids[i], names != null && i < names.Length && !string.IsNullOrEmpty(names[i]) ? names[i] : ids[i]);
                }
            RuleEngine.ApplyOrder(order);
        }

        [PunRPC]
        public void DrunkTextRPC(string name, string line, PhotonMessageInfo info = default)
        {
            if (!SemiFunc.MasterOnlyRPC(info)) return;
            DrinkHud.Instance?.AddDrunkFeed(name, line);
        }

        [PunRPC]
        public void TabSnapshotRPC(string data, PhotonMessageInfo info = default)
        {
            if (!SemiFunc.MasterOnlyRPC(info)) return;
            BarTabStats.ApplyRunSnapshot(data);
        }

        // ---- late join: catch the new player up with the current tab ----

        public override void OnPlayerEnteredRoom(Player newPlayer)
        {
            if (PhotonNetwork.IsMasterClient) SendSnapshot(newPlayer, BarTabStats.SerializeRun());
        }
    }
}
