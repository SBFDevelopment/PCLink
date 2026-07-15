using UdonSharp;
using UnityEngine;
using VRC.SDK3.StringLoading;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace com.SBFDevelopment.PCLink
{
    /// <summary>
    /// Transfer logic enum, represents how passing automatic ownership should work
    /// </summary>
    enum TransferLogic
    {
        /// <summary>
        /// Use the first found player.
        /// This matches FirstJoined but is not guaranteed to be true in the future.
        /// This should be the preferred method.
        /// </summary>
        FirstFound,
        /// <summary>
        /// Use the player that joined first.
        /// </summary>
        FirstJoined,
        /// <summary>
        /// Use the player that joined last.
        /// </summary>
        LastJoined
    }
    
    /// <summary>
    /// Class that strictly maintains object ownership only to players that qualify.
    /// Qualification depends on response from localhost url and if OnReceiveTransferData returns true
    /// </summary>
    class OwnerShipManager : UdonSharpBehaviour
    {
        #region Settings
        /// <summary>
        /// The logic to use when attempting to find a valid player
        /// </summary>
        [SerializeField] private TransferLogic transferLogic = TransferLogic.FirstFound;
        /// <summary>
        /// List of players that are allowed to own the object.
        /// This is a "soft" check, proper ownership still depends on the response from localhost url and if OnReceiveTransferData returns true.
        /// An empty list means every player will be tested if they can own the object
        /// </summary>
        [SerializeField] private string[] allowedPlayers = { };
        /// <summary>
        /// The url to test ownership against, apart from the port this shouldn't be changed
        /// </summary>
        [SerializeField] private VRCUrl localhost = new VRCUrl("http://localhost:1254");
        #endregion

        #region Private variables
        private bool _isIntendedOwner = false;
        #endregion

        #region Public API
        /// <summary>
        /// The "proper" way to request ownership, calling this function is the only way to receive ownership intentionally
        /// </summary>
        public void RequestOwnership()
        {
            if (!InList(Networking.LocalPlayer.displayName)) return;
            
            VRCStringDownloader.LoadUrl(localhost, this);
        }

        /// <summary>
        /// Requests a reset of the ownership, this will reset the system that looks for a valid owner.
        /// Helpful if we have exhausted the list and we suspect an earlier player on the list is able to take ownership.
        /// Ideally this function should be bound to a button in the world.
        /// </summary>
        public void RequestResetOwnership()
        {
            if (!InList(Networking.LocalPlayer.displayName)) return;
            
            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(ResetOwnership));
        }
        #endregion
        
        #region Virtual methods
        /// <summary>
        /// Function for validating whether the data returned by the URL would allow the player to take ownership
        /// </summary>
        /// <param name="data">The "key" to test if the player is allowed to take ownership</param>
        /// <returns>true if the player is allowed to take ownership, false otherwise</returns>
        protected virtual bool OnReceiveTransferData(string data)
        {
            return true;
        }
        #endregion

        #region Network methods
        /// <summary>
        /// Only runnable by the Owner and the player calling this function must be whitelisted to do so (in the "allowedPlayers" list)
        /// forcefully passes ownership to the player that would logically be the first in the order dictated by TransferLogic
        /// </summary>
        [NetworkCallable]
        public void ResetOwnership()
        {
            if (Networking.LocalPlayer != Networking.GetOwner(gameObject) || !InList(NetworkCalling.CallingPlayer.displayName)) return;
            
            var players = GetSortedPlayerList(transferLogic == TransferLogic.LastJoined);
    
            // Find the very first valid player in the sorted priority list
            foreach (var player in players)
            {
                if (player == null || !player.IsValid() || !InList(player.displayName)) continue;

                // Forcefully pass ownership to the start of the chain.
                // They will receive ownership, realize they are not '_isIntendedOwner',
                // and immediately kick off their own 'localhost' verification!
                Networking.SetOwner(player, gameObject);
                break;
            }
        }
        
        /// <summary>
        /// Only runnable by the Owner and the player calling this function must be whitelisted to do so (in the "allowedPlayers" list).
        /// This is the "correct" path to transfer ownership of the object.
        /// Transfer will fail if OnReceiveTransferData(data) returns false.
        /// </summary>
        /// <param name="data">data to pass to OnReceiveTransferData to test if ownership is allowed to pass</param>
        [NetworkCallable]
        public void TransferOwnership(string data)
        {
            if (!InList(NetworkCalling.CallingPlayer.displayName) || !Networking.LocalPlayer.IsOwner(gameObject))
            {
                return;
            }

            if (OnReceiveTransferData(data))
            {
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(SignalOwnerShipChange), NetworkCalling.CallingPlayer);
                Networking.SetOwner(NetworkCalling.CallingPlayer, gameObject);
                return;
            }
        }
        
        /// <summary>
        /// Only callable by the owner.
        /// Signals to all users the object is about to change ownership and to whom.
        /// If the intendedOwner is the localplayer this will set _isIntendedOwner to true.
        /// _isIntendedOwner will be set to false otherwise.
        /// </summary>
        /// <param name="intendedOwner">The player the ownership is intended to be passed to</param>
        [NetworkCallable]
        public void SignalOwnerShipChange(VRCPlayerApi intendedOwner)
        {
            if (NetworkCalling.CallingPlayer != Networking.GetOwner(gameObject))
            {
                return;
            }

            _isIntendedOwner = Networking.LocalPlayer == intendedOwner;
        }
        #endregion

        #region Udon events
        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (Networking.LocalPlayer != Networking.GetOwner(gameObject))
                return;

            if (Networking.LocalPlayer == player)
            {
                if (transferLogic == TransferLogic.LastJoined)
                {
                    RequestOwnership();
                }

                return;
            }

            if (!_isIntendedOwner)
            {
                Networking.SetOwner(player, gameObject);
                return;
            }
        }

        public override bool OnOwnershipRequest(VRCPlayerApi requestingPlayer, VRCPlayerApi requestedOwner)
        {
            return requestingPlayer == Networking.GetOwner(gameObject);
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            // Have we received ownership?
            if (Networking.LocalPlayer != player)
            {
                _isIntendedOwner = false;
                return;
            }

            // Are we the intended owner?
            if (_isIntendedOwner)
            {
                return;
            }

            // Do we qualify as a potential owner?
            if (InList(Networking.LocalPlayer.displayName))
            {
                VRCStringDownloader.LoadUrl(localhost, this);
                return;
            }

            // Is there someone to pass ownership to?
            var nextValidOwner = GetNextValidOwner();
            if (nextValidOwner != null)
            {
                Networking.SetOwner(nextValidOwner, gameObject);
                return;
            }
        }

        public override void OnStringLoadSuccess(IVRCStringDownload result)
        {
            if (!result.Url.Equals(localhost))
            {
                return;
            }

            // Are we the current owner?
            if (Networking.LocalPlayer != Networking.GetOwner(gameObject))
            {
                SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(TransferOwnership), result.Result);
                return;
            }

            // Do we qualify to take ownership?
            if (OnReceiveTransferData(result.Result)) return;

            // Is there someone to pass ownership to?
            var nextValidOwner = GetNextValidOwner();
            if (nextValidOwner != null)
            {
                Networking.SetOwner(nextValidOwner, gameObject);
                return;
            }
        }

        public override void OnStringLoadError(IVRCStringDownload result)
        {
            if (!result.Url.Equals(localhost) || Networking.LocalPlayer != Networking.GetOwner(gameObject))
                return;
            
            // Is there someone to pass ownership to?
            var nextValidOwner = GetNextValidOwner();
            if (nextValidOwner != null)
            {
                Networking.SetOwner(nextValidOwner, gameObject);
                return;
            }
        }
        #endregion

        #region Helpers
        /// <summary>
        /// Tests if the provided playername is in the list (if the list isn't empty)
        /// </summary>
        /// <param name="playerName">The name to test</param>
        /// <returns>True if playerName is in the allowedPlayers list or the list is empty, false otherwise</returns>
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

        /// <summary>
        /// Returns the next player that would qualify for ownership after the current player according to the set transferLogic
        /// </summary>
        /// <returns>The next player that would qualify for ownership after the current player</returns>
        private VRCPlayerApi GetNextValidOwner()
        {
            var players = GetSortedPlayerList(transferLogic == TransferLogic.LastJoined);
            
            var totalPlayers = players.Length;
            var startingIndex = 0;
            
            for (var i = 0; i < totalPlayers; i++)
            {
                if (players[i] == null || players[i].playerId != Networking.LocalPlayer.playerId) continue;
                startingIndex = i + 1;
                break;
            }

            for (var i = startingIndex; i < totalPlayers; i++)
            {
                var player = players[i];
                if (player == null || !player.IsValid() || !InList(player.displayName)) continue;
                return player;
            }
            
            return null;
        }
        
        /// <summary>
        /// Get a player list sorted based on time in instance
        /// </summary>
        /// <param name="reverse">Whether the list should go from youngest to oldest player</param>
        /// <returns>A player list sorted based on time in instance</returns>
        private VRCPlayerApi[] GetSortedPlayerList(bool reverse = false)
        {
            var players = VRCPlayerApi.GetPlayers();
            
            var n = players.Length;
    
            for (var i = 0; i < n - 1; i++)
            {
                var swapIndex = i;
        
                for (var j = i + 1; j < n; j++)
                {
                    if (players[j] == null || !players[j].IsValid()) continue;
                    if (players[swapIndex] == null || !players[swapIndex].IsValid())
                    {
                        swapIndex = j;
                        continue;
                    }

                    var checkResult = reverse
                        ? players[j].playerId > players[swapIndex].playerId
                        : players[j].playerId < players[swapIndex].playerId;
                    if (checkResult)
                    {
                        swapIndex = j;
                    }
                }

                // Swap the found player with the player at index i
                (players[swapIndex], players[i]) = (players[i], players[swapIndex]);
            }
            
            return players;
        }
        #endregion
    }
}