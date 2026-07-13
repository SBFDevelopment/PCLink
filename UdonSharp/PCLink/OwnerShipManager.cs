using UnityEngine;
using VRC.SDK3.StringLoading;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

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
        private bool _testingOwnOwnership;
        
        private bool InList(string playerName)
        {
            if (allowedPlayers.Length == 0)
                return true;
            
            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (var player in allowedPlayers)
                if (playerName == player)
                    return true;

            return false;
        }

        public void RequestOwnership()
        {
            if (!InList(Networking.LocalPlayer.displayName)) return;
            
            VRCStringDownloader.LoadUrl(localhost, this);
        }

        protected virtual bool OnReceiveTransferData(string data)
        {
            return true;
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            // We only care if WE are the ones who just joined
            if (player != Networking.LocalPlayer) return;

            VRCPlayerApi currentOwner = Networking.GetOwner(gameObject);

            // Condition A: If the logic dictates LastJoined, we should immediately request it
            if (transferLogic == TransferLogic.LastJoined)
            {
                RequestOwnership();
                return;
            }

            // Condition B: For any logic type, if the current owner shouldn't have it (invalid/not whitelisted),
            // and WE are whitelisted, let's step in and fix it.
            if (currentOwner != null && currentOwner.IsValid() && !InList(currentOwner.displayName))
            {
                // We qualify, the owner doesn't. Trigger a recovery check.
                RequestOwnership();
            }
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
            if (newOwner == null)
            {
                return;
            }
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
            VRCStringDownloader.LoadUrl(localhost, this);
        }

        private VRCPlayerApi[] GetSortedPlayerList()
        {
            var players = VRCPlayerApi.GetPlayers();
            
            var n = players.Length;
    
            for (var i = 0; i < n - 1; i++)
            {
                var minIndex = i;
        
                for (var j = i + 1; j < n; j++)
                {
                    // Handle potentially null or invalid slots in the array safely
                    if (players[j] == null || !players[j].IsValid()) continue;
                    if (players[minIndex] == null || !players[minIndex].IsValid())
                    {
                        minIndex = j;
                        continue;
                    }

                    // Lower playerId means they joined earlier
                    if (players[j].playerId < players[minIndex].playerId)
                    {
                        minIndex = j;
                    }
                }

                // Swap the found minimum player with the player at index i
                (players[minIndex], players[i]) = (players[i], players[minIndex]);
            }
            
            return players;
        }
        
        private VRCPlayerApi FindValidOwner(bool includeSelf = true, VRCPlayerApi startingPlayer = null)
        {
            var players = GetSortedPlayerList();
            
            var totalPlayers = players.Length;
            var startingIndex = transferLogic == TransferLogic.LastJoined ? totalPlayers : 0;

            if (startingPlayer != null && startingPlayer.IsValid())
            {
                for (var i = 0; i < totalPlayers; i++)
                {
                    if (players[i] == null || players[i].playerId != startingPlayer.playerId) continue;
                    startingIndex = i;
                    break;
                }
            }

            for (var i = startingIndex; i > 0 && i < totalPlayers; i += transferLogic == TransferLogic.LastJoined ? -1 : 1)
            {
                var player = players[i];

                if (player == null || !player.IsValid() || !InList(player.displayName)) continue;
                if (!includeSelf && player == Networking.LocalPlayer) continue;

                return player;
            }
            
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
            
            TransferOwner(FindValidOwner(false, Networking.LocalPlayer));
        }

        public override void OnStringLoadError(IVRCStringDownload result)
        {
            if (!result.Url.Equals(localhost) || !_testingOwnOwnership)
            {
                return;
            }
            
            TransferOwner(FindValidOwner(false, Networking.LocalPlayer));
        }
    }
}