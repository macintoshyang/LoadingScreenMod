﻿using System;
using System.Collections.Generic;
using ColossalFramework.Packaging;
using ColossalFramework.PlatformServices;
using UnityEngine;

namespace LoadingScreenModTest
{
    internal sealed class CustomDeserializer : Instance<CustomDeserializer>
    {
        internal int pathInfos, netInfos, buildingMeshInfos, vehicleMeshInfos;

        Package.Asset[] assets;
        Dictionary<PublishedFileId, HashSet<string>> packagesToPaths;
        readonly bool report = Settings.settings.reportAssets && Settings.settings.loadUsed;

        static Package.Asset[] Assets
        {
            get
            {
                if (instance.assets == null)
                    instance.assets = FilterAssets(Package.AssetType.Object);

                return instance.assets;
            }
        }

        private CustomDeserializer() { }

        internal void Dispose()
        {
            Fetch<PropInfo>.Dispose(); Fetch<TreeInfo>.Dispose(); Fetch<VehicleInfo>.Dispose(); Fetch<CitizenInfo>.Dispose();
            Fetch<NetInfo>.Dispose(); Fetch<BuildingInfo>.Dispose();
            assets = null; packagesToPaths = null; instance = null;
        }

        internal static object CustomDeserialize(Package p, Type t, PackageReader r)
        {
            // Props and trees in buildings and parks.
            if (t == typeof(BuildingInfo.Prop))
            {
                PropInfo pi = Get<PropInfo>(r.ReadString()); // old name format (without package name) is possible
                TreeInfo ti = Get<TreeInfo>(r.ReadString()); // old name format (without package name) is possible

                if (instance.report && UsedAssets.instance.GotAnyContainer())
                {
                    if (pi != null)
                    {
                        string n = pi.gameObject.name;

                        if (!string.IsNullOrEmpty(n) && n.IndexOf('.') >= 0)
                            UsedAssets.instance.IndirectProps.Add(n);
                    }

                    if (ti != null)
                    {
                        string n = ti.gameObject.name;

                        if (!string.IsNullOrEmpty(n) && n.IndexOf('.') >= 0)
                            UsedAssets.instance.IndirectTrees.Add(n);
                    }
                }

                return new BuildingInfo.Prop
                {
                    m_prop = pi,
                    m_tree = ti,
                    m_position = r.ReadVector3(),
                    m_angle = r.ReadSingle(),
                    m_probability = r.ReadInt32(),
                    m_fixedHeight = r.ReadBoolean()
                };
            }

            if (t == typeof(Package.Asset))
                return p.FindByChecksum(r.ReadString());

            // It seems that trailers are listed in the save game so this is not necessary. Better to be safe however
            // because a missing trailer reference is fatal for the simulation thread.
            if (t == typeof(VehicleInfo.VehicleTrailer))
            {
                string name = r.ReadString();
                string fullName = p.packageName + "." + name;
                VehicleInfo vi = Get<VehicleInfo>(p, fullName, name, false);

                VehicleInfo.VehicleTrailer trailer;
                trailer.m_info = vi;
                trailer.m_probability = r.ReadInt32();
                trailer.m_invertProbability = r.ReadInt32();
                return trailer;
            }

            // Paths (nets) in buildings.
            if (t == typeof(BuildingInfo.PathInfo))
            {
                string fullName = r.ReadString();
                instance.pathInfos++;
                Util.DebugPrint("  BuildingInfo.PathInfo:", p.packageName, p.packagePath, fullName);
                NetInfo ni = Get<NetInfo>(fullName);
                BuildingInfo.PathInfo path = new BuildingInfo.PathInfo();
                path.m_netInfo = ni;
                path.m_nodes = r.ReadVector3Array();
                path.m_curveTargets = r.ReadVector3Array();
                path.m_invertSegments = r.ReadBoolean();
                path.m_maxSnapDistance = r.ReadSingle();

                if (p.version >= 5)
                {
                    path.m_forbidLaneConnection = r.ReadBooleanArray();
                    path.m_trafficLights = (BuildingInfo.TrafficLights[]) (object) r.ReadInt32Array();
                    path.m_yieldSigns = r.ReadBooleanArray();
                }

                return path;
            }

            // Sub-buildings in buildings.
            if (t == typeof(BuildingInfo.SubInfo))
            {
                string name = r.ReadString();
                string fullName = p.packageName + "." + name;
                BuildingInfo bi = null;

                if (fullName == AssetLoader.instance.Current.fullName || name == AssetLoader.instance.Current.fullName)
                    Util.DebugPrint("Warning:", fullName, "wants to be a sub-building for itself");
                else
                    bi = Get<BuildingInfo>(p, fullName, name, true);

                BuildingInfo.SubInfo subInfo = new BuildingInfo.SubInfo();
                subInfo.m_buildingInfo = bi;
                subInfo.m_position = r.ReadVector3();
                subInfo.m_angle = r.ReadSingle();
                subInfo.m_fixedHeight = r.ReadBoolean();
                return subInfo;
            }

            // Prop variations in props.
            if (t == typeof(PropInfo.Variation))
            {
                string name = r.ReadString();
                string fullName = p.packageName + "." + name;
                PropInfo pi = null;

                if (fullName == AssetLoader.instance.Current.fullName)
                    Util.DebugPrint("Warning:", fullName, "wants to be a prop variation for itself");
                else
                    pi = Get<PropInfo>(p, fullName, name, false);

                return new PropInfo.Variation
                {
                    m_prop = pi,
                    m_probability = r.ReadInt32()
                };
            }

            // Tree variations in trees.
            if (t == typeof(TreeInfo.Variation))
            {
                string name = r.ReadString();
                string fullName = p.packageName + "." + name;
                TreeInfo ti = null;

                if (fullName == AssetLoader.instance.Current.fullName)
                    Util.DebugPrint("Warning:", fullName, "wants to be a tree variation for itself");
                else
                    ti = Get<TreeInfo>(p, fullName, name, false);

                return new TreeInfo.Variation
                {
                    m_tree = ti,
                    m_probability = r.ReadInt32()
                };
            }

            if (t == typeof(VehicleInfo.MeshInfo))
            {
                VehicleInfo.MeshInfo meshinfo = new VehicleInfo.MeshInfo();
                string checksum = r.ReadString();

                if (!string.IsNullOrEmpty(checksum))
                {
                    instance.vehicleMeshInfos++;
                    Util.DebugPrint("  VehicleInfo.MeshInfo:", p.packageName, p.packagePath, checksum);
                    Package.Asset asset = p.FindByChecksum(checksum);
                    GameObject go = AssetDeserializer.Instantiate(asset) as GameObject;
                    meshinfo.m_subInfo = go.GetComponent<VehicleInfoBase>();
                    go.SetActive(false);

                    if (meshinfo.m_subInfo.m_lodObject != null)
                        meshinfo.m_subInfo.m_lodObject.SetActive(false);
                }
                else
                    meshinfo.m_subInfo = null;

                meshinfo.m_vehicleFlagsForbidden = (Vehicle.Flags) PackageHelper.CustomDeserialize(p, typeof(Vehicle.Flags), r);
                meshinfo.m_vehicleFlagsRequired = (Vehicle.Flags) PackageHelper.CustomDeserialize(p, typeof(Vehicle.Flags), r);
                meshinfo.m_parkedFlagsForbidden = (VehicleParked.Flags) PackageHelper.CustomDeserialize(p, typeof(VehicleParked.Flags), r);
                meshinfo.m_parkedFlagsRequired = (VehicleParked.Flags) PackageHelper.CustomDeserialize(p, typeof(VehicleParked.Flags), r);
                return meshinfo;
            }

            if (t == typeof(BuildingInfo.MeshInfo))
            {
                instance.buildingMeshInfos++;
                Util.DebugPrint("  BuildingInfo.MeshInfo:", p.packageName, p.packagePath);
            }

            if (t == typeof(NetInfo))
            {
                string fullName = r.ReadString();
                Package.Asset container = AssetLoader.instance.Current;
                CustomAssetMetaData.Type type = AssetLoader.instance.GetMetaType(container.fullName);
                instance.netInfos++;

                if (type == CustomAssetMetaData.Type.Road || type == CustomAssetMetaData.Type.RoadElevation)
                {
                    Util.DebugPrint("  NetInfo A:", p.packageName, p.packagePath, fullName);
                    return Get<NetInfo>(p.packageName + "." + PackageHelper.StripName(fullName));
                }
                else
                {
                    Util.DebugPrint("  NetInfo B:", p.packageName, p.packagePath, fullName);
                    return Get<NetInfo>(fullName);
                }
            }

            if (t == typeof(BuildingInfo))
            {
                Package.Asset asset = p.Find(p.packageMainAsset);
                CustomAssetMetaData customAssetMetaData = (!(asset != null)) ? null : asset.Instantiate<CustomAssetMetaData>();
                string fullName = r.ReadString();

                if (customAssetMetaData != null && customAssetMetaData.type == CustomAssetMetaData.Type.Road)
                {
                    Util.DebugPrint("  BuildingInfo A:", p.packageName, fullName);
                    return PrefabCollection<BuildingInfo>.FindLoaded(p.packageName + "." + PackageHelper.StripName(fullName));
                }

                Util.DebugPrint("  BuildingInfo B:", fullName);
                return PrefabCollection<BuildingInfo>.FindLoaded(fullName);
            }

            return PackageHelper.CustomDeserialize(p, t, r);
        }

        // Works with (fullName = asset name), too.
        static T Get<T>(string fullName) where T : PrefabInfo
        {
            if (string.IsNullOrEmpty(fullName))
                return null;

            T info = FindLoaded<T>(fullName);

            if (info == null && Load(ref fullName, FindAsset(fullName)))
                info = FindLoaded<T>(fullName);

            return info;
        }

        // For sub-buildings, name may be package.assetname.
        static T Get<T>(Package package, string fullName, string name, bool tryName) where T : PrefabInfo
        {
            T info = FindLoaded<T>(fullName);

            if (tryName && info == null)
                info = FindLoaded<T>(name);

            if (info == null)
            {
                Package.Asset data = package.Find(name);

                if (tryName && data == null)
                    data = FindAsset(name); // yes, name

                if (data != null)
                    fullName = data.fullName;
                else if (name.IndexOf('.') >= 0)
                    fullName = name;

                if (Load(ref fullName, data))
                    info = FindLoaded<T>(fullName);
            }

            return info;
        }

        // Optimized version.
        internal static T FindLoaded<T>(string fullName) where T : PrefabInfo
        {
            if (string.IsNullOrEmpty(fullName))
                return null;

            Dictionary<string, PrefabCollection<T>.PrefabData> prefabDict = Fetch<T>.PrefabDict;
            PrefabCollection<T>.PrefabData prefabData;

            if (prefabDict.TryGetValue(fullName, out prefabData))
                return prefabData.m_prefab;

            // Old (early 2015) name?
            if (fullName.IndexOf('.') < 0)
            {
                Package.Asset[] a = Assets;

                for (int i = 0; i < a.Length; i++)
                    if (fullName == a[i].name && prefabDict.TryGetValue(a[i].package.packageName + "." + fullName, out prefabData))
                        return prefabData.m_prefab;
            }

            return null;
        }

        /// <summary>
        /// Given packagename.assetname, find the asset. Works with (fullName = asset name), too.
        /// </summary>
        internal static Package.Asset FindAsset(string fullName)
        {
            try
            {
                int j = fullName.IndexOf('.');

                if (j > 0 && j < fullName.Length - 1)
                {
                    // The fast path.
                    Package.Asset asset = instance.FindByName(fullName.Substring(0, j), fullName.Substring(j + 1));

                    if (asset != null)
                        return asset;
                }

                // Fast fail.
                if (AssetLoader.instance.HasFailed(fullName))
                    return null;

                Package.Asset[] a = Assets;

                // We also try the old (early 2015) naming that does not contain the package name. FindLoaded does this, too.
                for (int i = 0; i < a.Length; i++)
                    if (fullName == a[i].fullName || fullName == a[i].name)
                        return a[i];
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
            }

            return null;
        }

        Package.Asset FindByName(string packageName, string assetName)
        {
            PublishedFileId id = Util.GetPackageId(packageName);

            if (id != PublishedFileId.invalid)
            {
                if (packagesToPaths == null || packagesToPaths.Count == 0)
                    packagesToPaths = (Dictionary<PublishedFileId, HashSet<string>>) Util.GetStatic(typeof(PackageManager), "m_PackagesSteamToPathsMap");

                HashSet<string> paths;

                if (packagesToPaths.TryGetValue(id, out paths))
                {
                    Package package; Package.Asset asset;

                    foreach (string path in paths)
                        if ((package = PackageManager.FindPackageAt(path)) != null && (asset = package.Find(assetName)) != null)
                            return asset;
                }
            }

            return null;
        }

        static bool Load(ref string fullName, Package.Asset data)
        {
            if (data != null)
                try
                {
                    fullName = data.fullName;

                    // There is at least one asset (411236307) on the workshop that wants to include itself. Asset Editor quite
                    // certainly no longer accepts that but in the early days, it was possible.
                    if (fullName != AssetLoader.instance.Current.fullName && !AssetLoader.instance.HasFailed(fullName))
                    {
                        AssetLoader.instance.LoadImpl(data);
                        return true;
                    }
                }
                catch (Exception e)
                {
                    AssetLoader.instance.AssetFailed(fullName, e);
                }
            else
                AssetLoader.instance.NotFound(fullName);

            return false;
        }

        // Optimized version for other mods.
        static string ResolveCustomAssetName(string fullName)
        {
            // Old (early 2015) name?
            if (fullName.IndexOf('.') < 0)
            {
                Package.Asset[] a = CustomDeserializer.Assets;

                for (int i = 0; i < a.Length; i++)
                    if (fullName == a[i].name)
                        return a[i].package.packageName + "." + fullName;
            }

            return fullName;
        }

        static Package.Asset[] FilterAssets(Package.AssetType assetType)
        {
            List<Package.Asset> enabled = new List<Package.Asset>(64), notEnabled = new List<Package.Asset>(64);

            foreach (Package.Asset asset in PackageManager.FilterAssets(assetType))
                if (asset != null)
                    if (asset.isEnabled)
                        enabled.Add(asset);
                    else
                        notEnabled.Add(asset);

            // Why enabled assets first? Because in duplicate name situations, I want the enabled one to get through.
            Package.Asset[] ret = new Package.Asset[enabled.Count + notEnabled.Count];
            enabled.CopyTo(ret);
            notEnabled.CopyTo(ret, enabled.Count);
            enabled.Clear(); notEnabled.Clear();
            return ret;
        }
    }

    static class Fetch<T> where T : PrefabInfo
    {
        static Dictionary<string, PrefabCollection<T>.PrefabData> prefabDict;

        internal static Dictionary<string, PrefabCollection<T>.PrefabData> PrefabDict
        {
            get
            {
                if (prefabDict == null)
                    prefabDict = (Dictionary<string, PrefabCollection<T>.PrefabData>) Util.GetStatic(typeof(PrefabCollection<T>), "m_prefabDict");

                return prefabDict;
            }
        }

        internal static void Dispose() => prefabDict = null;
    }
}
