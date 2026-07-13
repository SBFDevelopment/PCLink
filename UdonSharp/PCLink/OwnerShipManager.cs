using UnityEngine;
using VRC.SDK3.StringLoading;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

// TODO: Implement FindValidOwner, this function should follow the logic set with transferLogic.
// TODO: Currently if no valid player is in the instance, the object will continuously pass between players, we should detect this and prevent it from happening.
// TODO: small security vulnerability which does allow a player to request ownership without going through the proper function path

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
        private VRCPlayerApi _intendedOwner;
        private bool _testingOwnOwnership = false;
        
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
        public void SignalOwnerChange(VRCPlayerApi owner)
        {
            if (NetworkCalling.CallingPlayer != Networking.GetOwner(gameObject))
            {
                return;
            }
            
            _intendedOwner = owner;
        }

        private void TransferOwner(VRCPlayerApi newOwner)
        {
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(SignalOwnerChange), newOwner);
            Networking.SetOwner(newOwner, gameObject);
        }
        
        [NetworkCallable]
        public void TransferOwnership(string data)
        {
            if (!InList(NetworkCalling.CallingPlayer.displayName) || !Networking.LocalPlayer.IsOwner(gameObject))
            {
                return;
            }

            if (OnReceiveTransferData(data))
            {
                TransferOwner(NetworkCalling.CallingPlayer);
            }
        }

        public override bool OnOwnershipRequest(VRCPlayerApi requestingPlayer, VRCPlayerApi requestedOwner)
        {
            return _intendedOwner == requestedOwner;
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            if (Networking.LocalPlayer != player || _intendedOwner == player)
            {
                _intendedOwner = null;
                return;
            }

            // we are the new owner
            // we might be the intended owner
            _intendedOwner = FindValidOwner();

            if (_intendedOwner != Networking.LocalPlayer)
            {
                // we aren't the intended owner
                // pass ownership to the next "supposed" owner, they will verify fully if they are the intended owner
                Networking.SetOwner(_intendedOwner, gameObject);
                _intendedOwner = null;
                return;
            }
            
            _testingOwnOwnership = true;
            VRCStringDownloader.LoadUrl(localhost, (IUdonEventReceiver)this);
        }

        private VRCPlayerApi FindValidOwner(bool includeSelf = true)
        {
            return null;
        }
        
        public override void OnStringLoadSuccess(IVRCStringDownload result)
        {
            if (!result.Url.Equals(localhost))
            {
                return;
            }

            if (!_testingOwnOwnership)
            {
                SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(TransferOwnership), result.Result);
            }

            _testingOwnOwnership = false;
            if (OnReceiveTransferData(result.Result)) return;
            
            TransferOwner(FindValidOwner(false));
        }

        public override void OnStringLoadError(IVRCStringDownload result)
        {
            if (!result.Url.Equals(localhost) || !_testingOwnOwnership)
            {
                return;
            }
            
            TransferOwner(FindValidOwner(false));
        }
    }
}