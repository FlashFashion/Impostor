﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Impostor.Api.Innersloth.GameData;
using Impostor.Api.Net.Messages;
using Impostor.Server.GameData;
using Impostor.Server.GameData.Objects;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Impostor.Server.Net.State
{
    internal partial class Game
    {
        private static readonly Type[] SpawnableObjects =
        {
            typeof(InnerShipStatus), // ShipStatus
            typeof(InnerMeetingHud),
            typeof(InnerLobbyBehaviour),
            typeof(InnerGameData),
            typeof(InnerPlayerControl),
            typeof(InnerShipStatus), // HeadQuarters
            typeof(InnerShipStatus), // PlanetMap
            typeof(InnerShipStatus), // AprilShipStatus
        };

        private readonly List<InnerNetObject> _allObjects = new List<InnerNetObject>();
        private readonly Dictionary<uint, InnerNetObject> _allObjectsFast = new Dictionary<uint, InnerNetObject>();

        private int _gamedataInitialized;

        public void InitGameData()
        {
            if (Interlocked.Exchange(ref _gamedataInitialized, 1) != 0)
            {
                return;
            }


        }

        public async ValueTask HandleGameDataAsync(IMessageReader parent, ClientPlayer sender, bool toPlayer)
        {
            // Find target player.
            ClientPlayer target = null;

            if (toPlayer)
            {
                var targetId = parent.ReadPackedInt32();
                if (!TryGetPlayer(targetId, out target))
                {
                    // Invalid target.
                    return;
                }
            }

            // Parse GameData messages.
            while (parent.Position < parent.Length)
            {
                var reader = parent.ReadMessage();

                switch (reader.Tag)
                {
                    case GameDataTag.DataFlag:
                    {
                        var netId = reader.ReadPackedUInt32();
                        if (_allObjectsFast.TryGetValue(netId, out var obj))
                        {
                            obj.Deserialize(reader, false);
                        }
                        else
                        {
                            _logger.LogWarning("Received DataFlag for unregistered NetId {0}.", netId);
                        }

                        break;
                    }

                    case GameDataTag.RpcFlag:
                    {
                        if (_allObjectsFast.TryGetValue(reader.ReadPackedUInt32(), out var obj))
                        {
                            // obj.HandleRpc(reader.ReadByte(), reader);
                        }

                        break;
                    }

                    case GameDataTag.SpawnFlag:
                    {
                        var objectId = reader.ReadPackedUInt32();
                        if (objectId < SpawnableObjects.Length)
                        {
                            var innerNetObject = (InnerNetObject) ActivatorUtilities.CreateInstance(_serviceProvider, SpawnableObjects[objectId], this);
                            var id = reader.ReadPackedInt32();

                            innerNetObject.SpawnFlags = (SpawnFlags) reader.ReadByte();

                            var components = innerNetObject.GetComponentsInChildren<InnerNetObject>();
                            var componentsCount = reader.ReadPackedInt32();

                            if (componentsCount != components.Count)
                            {
                                _logger.LogError(
                                    "Children didn't match for spawnable {0}, name {1} ({2} != {3})",
                                    objectId,
                                    innerNetObject.GetType().Name,
                                    componentsCount,
                                    components.Count);
                                continue;
                            }

                            for (var i = 0; i < componentsCount; i++)
                            {
                                var obj = components[i];

                                obj.NetId = reader.ReadPackedUInt32();
                                obj.OwnerId = id;

                                if (!AddNetObject(obj))
                                {
                                    _logger.LogTrace("Failed to AddNetObject.");

                                    obj.NetId = uint.MaxValue;
                                    break;
                                }

                                var readerSub = reader.ReadMessage();
                                if (readerSub.Length > 0)
                                {
                                    obj.Deserialize(readerSub, true);
                                }
                            }

                            if ((innerNetObject.SpawnFlags & SpawnFlags.IsClientCharacter) != SpawnFlags.None)
                            {
                                if (TryGetPlayer(id, out var clientById))
                                {
                                    _logger.LogTrace("Spawn character");
                                }
                                else
                                {
                                    _logger.LogTrace("Spawn unowned character");
                                }
                            }

                            continue;
                        }

                        _logger.LogError("Couldn't find spawnable object {0}.", objectId);
                        break;
                    }

                    case GameDataTag.DespawnFlag:
                    {
                        var objectNetId = reader.ReadPackedUInt32();
                        _logger.LogTrace("> Destroy {0}", objectNetId);
                        break;
                    }

                    case GameDataTag.SceneChangeFlag:
                    {
                        var clientId = reader.ReadPackedInt32();
                        var targetScene = reader.ReadString();
                        _logger.LogTrace("> Scene {0} to {1}", clientId, targetScene);
                        break;
                    }

                    case GameDataTag.ReadyFlag:
                    {
                        var clientId = reader.ReadPackedInt32();
                        _logger.LogTrace("> IsReady {0}", clientId);
                        break;
                    }

                    default:
                    {
                        _logger.LogTrace("Bad GameData tag {0}", reader.Tag);
                        break;
                    }
                }
            }
        }

        private bool AddNetObject(InnerNetObject obj)
        {
            if (_allObjectsFast.ContainsKey(obj.NetId))
            {
                return false;
            }

            _allObjects.Add(obj);
            _allObjectsFast.Add(obj.NetId, obj);
            return true;
        }

        private void RemoveNetObject(InnerNetObject obj)
        {
            var index = _allObjects.IndexOf(obj);
            if (index > -1)
            {
                _allObjects.RemoveAt(index);
            }

            _allObjectsFast.Remove(obj.NetId);

            obj.NetId = uint.MaxValue;
        }
    }
}