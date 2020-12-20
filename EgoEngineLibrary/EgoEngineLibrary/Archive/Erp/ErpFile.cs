﻿namespace EgoEngineLibrary.Archive.Erp
{
    using MiscUtil.Conversion;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;

    public class ErpFile
    {
        public Int32 Version { get; set; }

        public UInt64 ResourceOffset { get; set; }

        public List<ErpResource> Resources { get; set; }

        private UInt64 _resourceInfoTotalLength;
        public Progress<int>? ProgressPercentage;
        public Progress<string>? ProgressStatus;

        public ErpFile()
        {
            this.Version = 4;
            this.Resources = new List<ErpResource>();
        }

        public void Read(Stream stream)
        {
            using (ErpBinaryReader reader = new ErpBinaryReader(EndianBitConverter.Little, stream))
            {
                uint magic = reader.ReadUInt32();
                if (magic != 1263555141)
                {
                    throw new Exception("This is not an ERP file!");
                }

                this.Version = reader.ReadInt32();
                reader.ReadBytes(8); // padding
                reader.ReadBytes(8); // info offset
                reader.ReadBytes(8); // info size

                this.ResourceOffset = reader.ReadUInt64();
                reader.ReadBytes(8); // padding

                Int32 numFiles = reader.ReadInt32();
                Int32 numTempFile = reader.ReadInt32();

                for (int i = 0; i < numFiles; ++i)
                {
                    ErpResource entry = new ErpResource(this);
                    entry.Read(reader);
                    this.Resources.Add(entry);
                }
            }
        }

        public void Write(Stream stream)
        {
            using (ErpBinaryWriter writer = new ErpBinaryWriter(EndianBitConverter.Little, stream))
            {
                Int32 numTempFiles = this.UpdateOffsets();

                writer.Write(1263555141);

                writer.Write(this.Version);
                writer.Write((Int64)0);
                writer.Write((Int64)48);
                writer.Write(this._resourceInfoTotalLength);

                writer.Write(this.ResourceOffset);
                writer.Write((Int64)0);

                writer.Write(this.Resources.Count);
                writer.Write(numTempFiles);

                foreach (ErpResource entry in this.Resources)
                {
                    entry.Write(writer);
                }

                foreach (ErpResource entry in this.Resources)
                {
                    foreach (ErpFragment res in entry.Fragments)
                    {
                        //writer.Write((UInt16)0xDA78);
                        writer.Write(res._data);
                    }
                }
            }
        }

        public Int32 UpdateOffsets()
        {
            UInt64 resourceDataOffset = 0;
            Int32 numTempFiles = 0;

            this._resourceInfoTotalLength = (UInt64)this.Resources.Count * 4 + 8;
            foreach (ErpResource entry in this.Resources)
            {
                this._resourceInfoTotalLength += (UInt64)entry.UpdateOffsets();

                foreach (ErpFragment res in entry.Fragments)
                {
                    ++numTempFiles;
                    res.Offset = resourceDataOffset;
                    resourceDataOffset += res.PackedSize;
                }
            }

            this.ResourceOffset = 48 + this._resourceInfoTotalLength;
            return numTempFiles;
        }

        public ErpResource FindResource(string fileName)
        {
            var res = TryFindResource(fileName);

            if (res == null)
            {
                throw new InvalidOperationException($"Could not find resource: {fileName}");
            }

            return res;
        }
        public ErpResource? TryFindResource(string fileName)
        {
            foreach (ErpResource entry in this.Resources)
            {
                if (entry.Identifier == fileName)
                {
                    return entry;
                }
            }

            return null;
        }

        public void Export(string folderPath)
        {
            int success = 0;
            int fail = 0;

            for (int i = 0; i < Resources.Count;)
            {
                (ProgressStatus as IProgress<string>)?.Report("Exporting " + Path.Combine(Resources[i].Folder, Resources[i].FileName) + "... ");

                try
                {
                    Resources[i].Export(folderPath);
                    (ProgressStatus as IProgress<string>)?.Report("SUCCESS" + Environment.NewLine);
                    ++success;
                }
                catch
                {
                    (ProgressStatus as IProgress<string>)?.Report("FAIL" + Environment.NewLine);
                    ++fail;
                }

                ++i;
                (ProgressPercentage as IProgress<int>)?.Report(i);
            }

            (ProgressStatus as IProgress<string>)?.Report(string.Format("{0} Succeeded, {1} Failed", success, fail));
        }

        public void Import(string[] files)
        {
            int success = 0;
            int fail = 0;
            int skip = 0;

            for (int i = 0; i < Resources.Count;)
            {
                (ProgressStatus as IProgress<string>)?.Report("Importing " + Path.Combine(Resources[i].Folder, Resources[i].FileName) + "... ");

                try
                {
                    if (Resources[i].Import(files))
                    {
                        (ProgressStatus as IProgress<string>)?.Report("SUCCESS" + Environment.NewLine);
                        ++success;
                    }
                    else
                    {
                        (ProgressStatus as IProgress<string>)?.Report("SKIP" + Environment.NewLine);
                        ++skip;
                    }
                }
                catch
                {
                    (ProgressStatus as IProgress<string>)?.Report("FAIL" + Environment.NewLine);
                    ++fail;
                }

                ++i;
                (ProgressPercentage as IProgress<int>)?.Report(i);
            }

            (ProgressStatus as IProgress<string>)?.Report(string.Format("{0} Succeeded, {1} Skipped, {2} Failed", success, skip, fail));
        }
    }
}
