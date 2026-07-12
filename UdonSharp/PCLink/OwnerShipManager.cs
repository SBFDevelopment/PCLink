
using UnityEngine;
using VRC.SDK3.StringLoading;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace com.SBFDevelopment.PCLink
{
    enum TransferLogic
    {
        FirstFound,
        FirstJoined,
        LastJoined
    }
    
    class OwnerShipManager : Base
    {
        [SerializeField] private TransferLogic transferLogic = TransferLogic.FirstFound;
        [SerializeField] private string[] allowedPlayers = { };
        [SerializeField] private VRCUrl localhost = new VRCUrl("http://localhost:1254");
        private VRCPlayerApi _cachedOwner;
        
        private bool InList(string playerName)
        {
            if (allowedPlayers.Length == 0)
                return true;
            
            foreach (var player in allowedPlayers)
                if (playerName == player)
                    return true;

            return false;
        }

        public void RequestOwnership()
        {
            if (InList(Networking.LocalPlayer.displayName))
            {
                VRCStringDownloader.LoadUrl(localhost, (IUdonEventReceiver)this);
                return;
            }
        }

        protected virtual bool OnReceiveTransferData(string data)
        {
            return true;
        }
        
        [NetworkCallable]
        public void TransferOwnership(string data)
        {
            if (!InList(NetworkCalling.CallingPlayer.displayName))
            {
                return;
            }

            if (OnReceiveTransferData(data))
            {
                Networking.SetOwner(NetworkCalling.CallingPlayer, gameObject);
            }
        }

        public override bool OnOwnershipRequest(VRCPlayerApi requestingPlayer, VRCPlayerApi requestedOwner)
        {
            return InList(requestedOwner.displayName);
        }

        public override void OnStringLoadSuccess(IVRCStringDownload result)
        {
            if (result.Url.Equals(localhost))
            {
                SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(TransferOwnership), result.Result);
                return;
            }
        }
    }
}