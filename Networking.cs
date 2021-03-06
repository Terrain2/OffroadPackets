using System;
using HarmonyLib;
using Steamworks;
using Terrain.Packets.Plugin;
using UnityEngine;

namespace Terrain.Packets.LowLevelNetworking
{
    internal static class Send
    {
        internal static void ToServer(Packet packet, P2PSend type)
        {
            ClientSend.bytesSent += packet.Length();
            ClientSend.packetsSent++;
            packet.WriteLength();
            if (NetworkController.Instance.networkType == NetworkController.NetworkType.Classic)
            {
                LocalClient.instance.tcp.SendData(packet);
            }
            else
            {
                SteamPacketManager.SendPacket(LocalClient.instance.serverHost, packet, type, SteamPacketManager.NetworkChannel.ToServer);
            }
        }

        internal static void To(int toClient, Packet packet, P2PSend type)
        {
            packet.WriteLength();
            if (NetworkController.Instance.networkType == NetworkController.NetworkType.Classic)
            {
                Server.clients[toClient].tcp.SendData(packet);
            }
            else
            {
                SteamPacketManager.SendPacket(Server.clients[toClient].player.steamId, packet, type, SteamPacketManager.NetworkChannel.ToClient);
            }
        }

        internal static void ToAll(Packet packet, P2PSend type)
        {
            packet.WriteLength();
            if (NetworkController.Instance.networkType == NetworkController.NetworkType.Classic)
            {
                for (int i = 1; i < Server.MaxPlayers; i++)
                {
                    Server.clients[i].tcp.SendData(packet);
                }
                return;
            }
            foreach (Client client in Server.clients.Values)
            {
                if (client?.player != null)
                {
                    SteamPacketManager.SendPacket(client.player.steamId, packet, type, SteamPacketManager.NetworkChannel.ToClient);
                }
            }
        }

        internal static void ToAllExcept(int exceptClient, Packet packet, P2PSend type)
        {
            packet.WriteLength();
            if (NetworkController.Instance.networkType == NetworkController.NetworkType.Classic)
            {
                for (int i = 1; i < Server.MaxPlayers; i++)
                {
                    if (i != exceptClient)
                    {
                        Server.clients[i].tcp.SendData(packet);
                    }
                }
                return;
            }
            foreach (Client client in Server.clients.Values)
            {
                if (client?.player != null && SteamLobby.steamIdToClientId[client.player.steamId] != exceptClient)
                {
                    SteamPacketManager.SendPacket(client.player.steamId, packet, type, SteamPacketManager.NetworkChannel.ToClient);
                }
            }
        }

        internal static void ToAllExcept(int[] exceptClients, Packet packet, P2PSend type)
        {
            packet.WriteLength();
            if (NetworkController.Instance.networkType == NetworkController.NetworkType.Classic)
            {
                for (int i = 1; i < Server.MaxPlayers; i++)
                {
                    bool flag = false;
                    foreach (int num in exceptClients)
                    {
                        if (i == num)
                        {
                            flag = true;
                        }
                    }
                    if (!flag)
                    {
                        Server.clients[i].tcp.SendData(packet);
                    }
                }
                return;
            }
            foreach (Client client in Server.clients.Values)
            {
                if (client?.player != null)
                {
                    bool flag2 = false;
                    foreach (int num2 in exceptClients)
                    {
                        if (SteamLobby.steamIdToClientId[client.player.steamId] == num2)
                        {
                            flag2 = true;
                        }
                    }
                    if (!flag2)
                    {
                        SteamPacketManager.SendPacket(client.player.steamId, packet, type, SteamPacketManager.NetworkChannel.ToClient);
                    }
                }
            }
        }
    }

    [HarmonyPatch]
    static class Handle
    {
        static void BadServerPacket(string guid, string name = null)
        {
            if (name == null)
            {
                Main.Error($"Server sent packet from unknown guid {guid}");
            }
            else
            {
                Main.Error($"Server sent unknown packet {guid}/{name}");
            }

            ImportantPackets.ClientSend.BadPacket(guid, name);
        }

        static void BadClientPacket(int fromClient, string guid, string name = null)
        {
            var username = Server.clients[fromClient].player.username;
            if (name == null)
            {
                Main.Error($"{username} sent packet from unknown guid {guid}");
            }
            else
            {
                Main.Error($"{username} sent unknown packet {guid}/{name}");
            }

            ImportantPackets.ServerSend.BadPacket(fromClient, guid, name);
        }

        [HarmonyPatch(typeof(LocalClient), nameof(LocalClient.InitializeClientData)), HarmonyPostfix]
        static void ClientHandle()
        {
            LocalClient.packetHandlers[0] = packet =>
            {
                try
                {
                    var guid = packet.ReadString();
                    if (!OffroadPackets.Instances.TryGetValue(guid, out var packets))
                    {
                        BadServerPacket(guid);
                        return;
                    }
                    var name = packet.ReadString();
                    if (!packets.ServerPacketHandlers.TryGetValue(name, out var handler))
                    {
                        BadServerPacket(guid, name);
                        return;
                    }
                    var bytes = packet.ReadBytes(packet.UnreadLength());
                    handler(bytes);
                }
                catch (Exception ex)
                {
                    // this is because the game also ignored any errors in all packet handlers on the client
                    // i assume that's because when the server sends packets to the local client
                    // it uses direct function calls instead of network requests
                    // and an exception would bubble up to the server send
                    // and if that packet was intended for everyone it could prevent some people from getting it
                    // and that can't happen with remote players, so why should it for the local player?
                    // TL;DR exceptions on local player shouldn't bubble up to the server, so they have to be caught
                    Debug.LogError(ex);
                }
            };
        }

        [HarmonyPatch(typeof(Server), nameof(Server.InitializeServerPackets)), HarmonyPostfix]
        static void ServerHandle()
        {
            Server.PacketHandlers[0] = (fromClient, packet) =>
            {
                try
                {
                    var guid = packet.ReadString();
                    if (!OffroadPackets.Instances.TryGetValue(guid, out var packets))
                    {
                        BadClientPacket(fromClient, guid);
                        return;
                    }
                    var name = packet.ReadString();
                    if (!packets.ClientPacketHandlers.TryGetValue(name, out var handler))
                    {
                        BadClientPacket(fromClient, guid, name);
                        return;
                    }
                    var bytes = packet.ReadBytes(packet.UnreadLength());
                    handler(fromClient, bytes);
                }
                catch (Exception ex)
                {
                    // vanilla packets don't catch server errors and it's not as important, but i might as well just in case
                    Debug.LogError(ex);
                }
            };
        }
    }
}