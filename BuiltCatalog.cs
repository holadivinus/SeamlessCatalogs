using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

public class BuiltCatalog
{
    public BuiltCatalog(string catPath)
    {
        CatPath = catPath;
        Data = JsonConvert.DeserializeObject<CatData>(File.ReadAllText(catPath));   

        byte[] entriesAsBytes = Convert.FromBase64String(Data.m_EntryDataString);
        for (int i = 0; i < entriesAsBytes.Length-4; i++)
        {
            if (i % EntryData.ByteSize != 0) continue;
            EntryDatas.Add(new EntryData(entriesAsBytes, i+4, this));
        }

        byte[] bucketAsBytes = Convert.FromBase64String(Data.m_BucketDataString);
        int deserzState = 0;
        int curBucket = -1;
        int curUsedEntryRef = 0;
        BucketData[] loadingBuckets = null;
        for (int i = 0; i < bucketAsBytes.Length; i+=4)
        {
            switch (deserzState)
            {
                case 0: // bucketCount
                    loadingBuckets = new BucketData[BitConverter.ToInt32(bucketAsBytes, 0)];
                    deserzState++;
                    continue;
                case 1: // keyOffset
                    curBucket++;
                    loadingBuckets[curBucket] = new BucketData(this);
                    loadingBuckets[curBucket].keyOffset = BitConverter.ToInt32(bucketAsBytes, i);
                    deserzState++;
                    continue;
                case 2: // entryCount
                    loadingBuckets[curBucket].entryCount = BitConverter.ToInt32(bucketAsBytes, i);
                    loadingBuckets[curBucket].usedEntryRefs = new int[loadingBuckets[curBucket].entryCount];
                    curUsedEntryRef = 0;
                    deserzState++;
                    continue;
                case 3: // usedEntryRefs
                    loadingBuckets[curBucket].usedEntryRefs[curUsedEntryRef] = BitConverter.ToInt32(bucketAsBytes, i);
                    curUsedEntryRef++;
                    if (curUsedEntryRef == loadingBuckets[curBucket].usedEntryRefs.Length)
                    {
                        deserzState = 1; // got it all done!
                    }
                    continue;
            }
        }
        Buckets = loadingBuckets.ToList();

        InternalIDs = Data.m_InternalIds.ToList();

        extraData = Convert.FromBase64String(Data.m_ExtraDataString).ToList();

    }
    public CatData Data;

    List<BucketData> Buckets;
    List<byte> extraData;


    // all that really changes are these
    List<EntryData> EntryDatas = new();
    List<string> InternalIDs;

    public string CatPath;
    public void SaveToFile()
    {
        var obj = JObject.Parse(File.ReadAllText(CatPath));

        List<byte> entryOut = new();
        entryOut.AddRange(BitConverter.GetBytes(EntryDatas.Count));
        foreach (var ed in EntryDatas) 
        {
            entryOut.AddRange(BitConverter.GetBytes(ed.InternalIdIdx));
            entryOut.AddRange(BitConverter.GetBytes(ed.ProviderIdIdx));
            entryOut.AddRange(BitConverter.GetBytes(ed.DependancyKeyIdx));
            entryOut.AddRange(BitConverter.GetBytes(ed.DependancyHash));
            entryOut.AddRange(BitConverter.GetBytes(ed.ExtraDataIdx));
            entryOut.AddRange(BitConverter.GetBytes(ed.PrimaryKeyIdx));
            entryOut.AddRange(BitConverter.GetBytes(ed.ResourceTypeIdx));
        }
        obj["m_EntryDataString"] = Convert.ToBase64String(entryOut.ToArray());

        obj["m_InternalIds"] = JToken.FromObject(InternalIDs.ToArray());

        obj["m_ExtraDataString"] = Convert.ToBase64String(extraData.ToArray());

        // serialize the buckets!!
        List<int> bucketDats = new()
        {
            Buckets.Count
        };
        foreach (var b in Buckets)
        {
            bucketDats.Add(b.keyOffset);
            bucketDats.Add(b.entryCount);
            foreach (var be in b.usedEntryRefs)
                bucketDats.Add(be);
        }

        obj["m_BucketDataString"] = Convert.ToBase64String(MemoryMarshal.AsBytes(bucketDats.ToArray().AsSpan()).ToArray());

        File.WriteAllText(CatPath, obj.ToString());
    }
    public void LogShit()
    {
        foreach (var item in EntryDatas)
        {
            Debug.Log(JsonConvert.SerializeObject(item));
        }
    }

    public class CatData
    {
        public string m_EntryDataString; // details basic info on each addressable
        public string m_BucketDataString; // IF an addressable has dependancies, the deps are detailed here
        public string[] m_InternalIds;
        public string m_KeyDataString;
        public string m_ExtraDataString;

        public string[] m_ProviderIds;
        public ResType[] m_resourceTypes;
        public class ResType
        {
            public string m_AssemblyName;
            public string m_ClassName;
        }
    }

    class EntryData
    {
        public const int ByteSize = 7 * 4;
        public EntryData(BuiltCatalog myCat) { Cat = myCat; }
        public EntryData(byte[] buffer, int offset, BuiltCatalog myCat)
        {
            InternalIdIdx = BitConverter.ToInt32(buffer, offset);
            ProviderIdIdx = BitConverter.ToInt32(buffer, offset+4);
            DependancyKeyIdx = BitConverter.ToInt32(buffer, offset+8);
            DependancyHash = BitConverter.ToInt32(buffer, offset+12);
            ExtraDataIdx = BitConverter.ToInt32(buffer, offset+16);
            PrimaryKeyIdx = BitConverter.ToInt32(buffer, offset + 20);
            ResourceTypeIdx = BitConverter.ToInt32(buffer, offset+24);
            Cat = myCat;
        }
        private BuiltCatalog Cat;

        public string InternalID => Cat.InternalIDs[InternalIdIdx];
        public BucketData ReffedDependancy => DependancyKeyIdx == -1 ? null : Cat.Buckets[DependancyKeyIdx];

        public int InternalIdIdx;
        public int ProviderIdIdx;
        public int DependancyKeyIdx; // idx into m_BucketDataString
        public int DependancyHash;
        public int ExtraDataIdx;
        public int PrimaryKeyIdx;
        public int ResourceTypeIdx;

        public byte[] GetBonusData()
        {
            if (ExtraDataIdx == -1) return null;

            int start = ExtraDataIdx;
            int end = Cat.extraData.Count;
            foreach (var ed in Cat.EntryDatas)
            {
                if (ed.ExtraDataIdx < 0) continue;
                if (ed.ExtraDataIdx > start && ed.ExtraDataIdx < end)
                    end = ed.ExtraDataIdx;
            }
            List<byte> o = new();
            for (int i = start; i < end; i++)
            {
                o.Add(Cat.extraData[i]);
            }
            return o.ToArray();
        }
    }
    class BucketData
    {
        public BucketData(BuiltCatalog myCat)
        {
            Cat = myCat;
        }
        private BuiltCatalog Cat;

        public int keyOffset; // maps to an Addressable primary key
        [JsonIgnore] public int entryCount;
        [JsonIgnore] public int[] usedEntryRefs; // maps to entries that primary key should load

        public string[] entryRefsIDs => usedEntryRefs.Select(i => Cat.EntryDatas[i].InternalID).ToArray();
    }

    public void AddDependancies(string localID, string remoteID, BuiltCatalog remoteCat)
    {
        EntryData localEntry = EntryDatas.FirstOrDefault(ed => ed.InternalID == localID);
        if (localEntry == null) return;
        int remoteIDIdx = InternalIDs.IndexOf(remoteID);
        if (remoteIDIdx == -1)
        {
            remoteIDIdx = InternalIDs.Count;
            InternalIDs.Add(remoteID);
        }
        
        
        // Find or Add EntryData for that internalID
        EntryData remoteEntry = EntryDatas.FirstOrDefault(ed => ed.InternalID == remoteID);
        if (remoteEntry == null)
        {
            // adding fake entry, gotta look like the real one!

            // this is what we want it to look like
            EntryData targetLookData = remoteCat.EntryDatas.First(ed => ed.InternalID == remoteID);
            int extraDataIdx = -1;
            if (targetLookData.ExtraDataIdx != -1) 
            {
                byte[] bonusData = targetLookData.GetBonusData();
                int existingIdx = extraData.ToArray().AsSpan().IndexOf(bonusData);
                if (existingIdx == -1)
                {
                    existingIdx = extraData.Count;
                    extraData.AddRange(bonusData);
                }
                extraDataIdx = existingIdx;
            }
            var template = EntryDatas[localEntry.ReffedDependancy.usedEntryRefs[0]];
            EntryDatas.Add(remoteEntry = new(this)
            {
                InternalIdIdx = remoteIDIdx,
                ProviderIdIdx = Array.IndexOf(Data.m_ProviderIds, "UnityEngine.ResourceManagement.ResourceProviders.AssetBundleProvider"),
                DependancyKeyIdx = -1,
                DependancyHash = 0,
                ExtraDataIdx = extraDataIdx,
                PrimaryKeyIdx = template.PrimaryKeyIdx,
                ResourceTypeIdx = Array.IndexOf(Data.m_resourceTypes, Data.m_resourceTypes.First(rt => rt.m_ClassName == "UnityEngine.ResourceManagement.ResourceProviders.IAssetBundleResource"))
            });
        }
        int remoteEntryIdx = EntryDatas.IndexOf(remoteEntry);

        // make localEntry depend on remote asset
        if (!localEntry.ReffedDependancy.usedEntryRefs.Contains(remoteEntryIdx))
        {
            localEntry.ReffedDependancy.usedEntryRefs = localEntry.ReffedDependancy.usedEntryRefs.Append(remoteEntryIdx).ToArray();
            localEntry.ReffedDependancy.entryCount = localEntry.ReffedDependancy.usedEntryRefs.Length;
        }


    }
}
