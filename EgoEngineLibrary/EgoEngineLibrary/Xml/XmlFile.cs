﻿namespace EgoEngineLibrary.Xml
{
    using MiscUtil.Conversion;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Xml;

    public enum XMLType
    {
        Text,
        BinXML,
        BXMLBig,
        BXMLLittle
    }

    public class XmlFile
    {
        public BinaryXmlString xmlStrings;
        public BinaryXmlElement[] xmlElements;
        public BinaryXmlAttribute[] xmlAttributes;
        public XMLType type;
        public XmlDocument doc;

        public XmlFile(System.IO.Stream fileStream)
        {
            // Test for XmlType
            try
            {
                doc = new XmlDocument();
                doc.Load(fileStream);
                type = XMLType.Text;
                foreach (XmlNode child in doc.ChildNodes)
                {
                    if (child.NodeType == XmlNodeType.Comment)
                    {
                        try
                        {
                            type = (XMLType)Enum.Parse(typeof(XMLType), child.Value);
                            break;
                        }
                        catch
                        {
                        }
                    }
                }
                return;
            }
            catch { doc = null; }
            finally
            {
                fileStream.Position = 0;
            }
            XmlBinaryReader r = new XmlBinaryReader(EndianBitConverter.Little, fileStream);
            byte headerByte = r.ReadByte();
            if (headerByte == 0x00)
            {
                type = XMLType.BXMLBig;
            }
            else if (headerByte == 0x01)
            {
                type = XMLType.BXMLLittle;
            }
            else
            {
                type = XMLType.BinXML;
            }
            r.BaseStream.Position = 0;
            r = null;

            // Create a text XML file
            if (type == XMLType.BinXML)
            {
                using (XmlBinaryReader reader = new XmlBinaryReader(EndianBitConverter.Little, fileStream))
                {
                    // Section 1
                    reader.ReadByte(); // Unique Byte
                    reader.ReadBytes(3); // Same Magic
                    reader.ReadInt32(); // File Length/Size

                    // Section 2
                    reader.ReadByte(); // Unique Byte
                    reader.ReadBytes(3); // Same Magic
                    reader.ReadInt32(); // Section 3 and 4 Total Length/Size

                    // Section 3 and 4
                    xmlStrings = new BinaryXmlString(reader);

                    // Section 5
                    reader.ReadInt32();
                    xmlElements = new BinaryXmlElement[reader.ReadInt32() / 24];
                    for (int i = 0; i < xmlElements.Length; i++)
                    {
                        xmlElements[i].elementNameID = reader.ReadInt32();
                        xmlElements[i].elementValueID = reader.ReadInt32();
                        xmlElements[i].attributeCount = reader.ReadInt32();
                        xmlElements[i].attributeStartID = reader.ReadInt32();
                        xmlElements[i].childElementCount = reader.ReadInt32();
                        xmlElements[i].childElementStartID = reader.ReadInt32();
                    }

                    // Section 6
                    reader.ReadInt32();
                    xmlAttributes = new BinaryXmlAttribute[reader.ReadInt32() / 8];
                    for (int i = 0; i < xmlAttributes.Length; i++)
                    {
                        xmlAttributes[i].nameID = reader.ReadInt32();
                        xmlAttributes[i].valueID = reader.ReadInt32();
                    }

                    // Build XML
                    doc = new XmlDocument();
                    doc.AppendChild(doc.CreateXmlDeclaration("1.0", "UTF-8", "yes"));
                    doc.AppendChild(doc.CreateComment(type.ToString()));
                    doc.AppendChild(xmlElements[0].CreateElement(doc, this));
                }
            }
            else if (type == XMLType.BXMLBig)
            {
                using (XmlBinaryReader reader = new XmlBinaryReader(EndianBitConverter.Big, fileStream))
                {
                    reader.ReadBytes(5);
                    doc = new XmlDocument();
                    doc.AppendChild(doc.CreateXmlDeclaration("1.0", "UTF-8", "yes"));
                    doc.AppendChild(doc.CreateComment(type.ToString()));
                    doc.AppendChild(reader.ReadBxmlElement(doc));
                }
            }
            else if (type == XMLType.BXMLLittle)
            {
                using (XmlBinaryReader reader = new XmlBinaryReader(EndianBitConverter.Little, fileStream))
                {
                    reader.ReadBytes(5);
                    doc = new XmlDocument();
                    doc.AppendChild(doc.CreateXmlDeclaration("1.0", "UTF-8", "yes"));
                    doc.AppendChild(doc.CreateComment(type.ToString()));
                    doc.AppendChild(reader.ReadBxmlElement(doc));
                }
            }
        }
        public void Write(System.IO.Stream stream)
        {
            Write(stream, type);
        }
        public void Write(System.IO.Stream fileStream, XMLType convertType)
        {
            if (convertType == XMLType.Text)
            {
                using (fileStream)
                {
                    doc.Save(fileStream);
                }
            }
            else if (convertType == XMLType.BinXML)
            {
                Dictionary<string, int> valuesToID = new Dictionary<string, int>();
                List<BinaryXmlElement> xmlElems = new List<BinaryXmlElement>();
                List<BinaryXmlAttribute> xmlAttrs = new List<BinaryXmlAttribute>();
                List<int> valueLocations = new List<int>();

                BuildBinXml(doc.DocumentElement, valuesToID, xmlElems, xmlAttrs);
                valueLocations.Add(0);

                using (XmlBinaryWriter writer = new XmlBinaryWriter(new LittleEndianBitConverter(), fileStream))
                {
                    writer.Write(0x7252221A);
                    writer.Write(0);

                    // Section 2: Sections 3 and 4 Header and Total Size
                    writer.Write(0x72522217);
                    writer.Write(0); // 16 as Extra, Rest from Section 3 and 4 Length

                    // Section 3: Values/Strings
                    writer.Write(0x7252221D);
                    writer.Write(0);
                    foreach (string s in valuesToID.Keys)
                    {
                        valueLocations.Add(writer.WriteTerminatedString(s) + valueLocations[valueLocations.Count - 1] + 1);
                    }
                    // Write zero bytes to make length the same way CM made it
                    int remainder = (int)writer.BaseStream.Position % 16;
                    byte[] pad = new byte[remainder > 8 ? 24 - remainder : 8 - remainder];
                    writer.Write(pad);

                    // Section 4: Value/String Locations
                    writer.Write(0x7252221E);
                    writer.Write(4 * valuesToID.Count);
                    for (int i = 0; i < valueLocations.Count - 1; i++)
                    {
                        writer.Write(valueLocations[i]);
                    }

                    // Section 5: Element Definitions
                    writer.Write(0x7252221B);
                    writer.Write(24 * xmlElems.Count);
                    for (int i = 0; i < xmlElems.Count; i++)
                    {
                        writer.WriteBinaryXmlElement(xmlElems[i]);
                    }

                    // Section 6: Attribute Definitions
                    writer.Write(0x7252221C);
                    writer.Write(8 * xmlAttrs.Count);
                    for (int i = 0; i < xmlAttrs.Count; i++)
                    {
                        writer.WriteBinaryXmlAttribute(xmlAttrs[i]);
                    }

                    // Update Section Lenghts/Sizes
                    writer.Seek(4, System.IO.SeekOrigin.Begin);
                    writer.Write((int)writer.BaseStream.Length - 8); // Section 1: Total File

                    writer.Seek(12, System.IO.SeekOrigin.Begin);
                    writer.Write(valueLocations[valueLocations.Count - 1] + 4 * valuesToID.Count + 16 + pad.Length); // Section 2

                    writer.Seek(20, System.IO.SeekOrigin.Begin);
                    writer.Write(valueLocations[valueLocations.Count - 1] + pad.Length); // Section 3
                }
            }
            else if (convertType == XMLType.BXMLBig)
            {
                using (XmlBinaryWriter writer = new XmlBinaryWriter(EndianBitConverter.Big, fileStream))
                {
                    writer.Write((byte)0);
                    writer.Write(Encoding.UTF8.GetBytes("BXML"));
                    writer.WriteBxmlElement(doc.DocumentElement);

                    // File Ending: "0004 06000000" x2
                    writer.Write((Int16)0x0004);
                    writer.Write((Int16)0x0600);
                    writer.Write((Int16)0);
                    writer.Write((Int16)0x0004);
                    writer.Write((Int16)0x0600);
                    writer.Write((Int16)0);
                }
            }
            else if (convertType == XMLType.BXMLLittle)
            {
                using (XmlBinaryWriter writer = new XmlBinaryWriter(EndianBitConverter.Little, fileStream))
                {
                    writer.Write((byte)1);
                    writer.Write(Encoding.UTF8.GetBytes("BXML"));
                    writer.WriteBxmlElement(doc.DocumentElement);

                    // File Ending: "0004 06000000" x2
                    writer.Write((Int16)0x0004);
                    writer.Write((Int16)0x0006);
                    writer.Write((Int16)0);
                    writer.Write((Int16)0x0004);
                    writer.Write((Int16)0x0006);
                    writer.Write((Int16)0);
                }
            }
        }

        public void BuildBinXml(XmlElement elem, Dictionary<string, int> valuesToID, List<BinaryXmlElement> xmlElems, List<BinaryXmlAttribute> xmlAttrs, int childElemIndex = 0)
        {
            if (childElemIndex == 0)
            {
                xmlElems.Add(new BinaryXmlElement());
            }
            BinaryXmlElement binElem = new BinaryXmlElement();
            int currentIndex = xmlElems.Count - 1;

            // Element Name String
            if (!valuesToID.ContainsKey(elem.Name))
            {
                binElem.elementNameID = valuesToID.Count;
                valuesToID.Add(elem.Name, valuesToID.Count);
            }
            else
            {
                binElem.elementNameID = valuesToID[elem.Name];
            }

            binElem.attributeStartID = elem.Attributes.Count > 0 ? xmlAttrs.Count : 0;
            binElem.attributeCount = elem.Attributes.Count;
            foreach (XmlAttribute attr in elem.Attributes)
            {
                BinaryXmlAttribute binAttr = new BinaryXmlAttribute();

                // Attribute Name String
                if (!valuesToID.ContainsKey(attr.Name))
                {
                    binAttr.nameID = valuesToID.Count;
                    valuesToID.Add(attr.Name, valuesToID.Count);
                }
                else
                {
                    binAttr.nameID = valuesToID[attr.Name];
                }

                // Attribute Value String
                if (!valuesToID.ContainsKey(attr.Value))
                {
                    binAttr.valueID = valuesToID.Count;
                    valuesToID.Add(attr.Value, valuesToID.Count);
                }
                else
                {
                    binAttr.valueID = valuesToID[attr.Value];
                }

                xmlAttrs.Add(binAttr);
            }

            // Element Value or Child Elements
            // Set its childElementStartID to the next available index for binary xml elements 
            // AKA the current count, will be overwritten if element actually has child nodes
            binElem.childElementStartID = xmlElems.Count;
            if (elem.HasChildNodes)
            {
                // Values inside of an element are considered child nodes like <element>ThisValue</element>
                // More Specifically they're called "XmlText" or Text Nodes
                if (elem.ChildNodes[0].NodeType == XmlNodeType.Text)
                {
                    //System.Windows.Forms.MessageBox.Show("daasda22222222222222222");
                    XmlText textNode = (XmlText)elem.ChildNodes[0];
                    if (textNode.Value != null)
                    {
                        if (!valuesToID.ContainsKey(textNode.Value))
                        {
                            binElem.elementValueID = valuesToID.Count;
                            valuesToID.Add(textNode.Value, valuesToID.Count);
                        }
                        else
                        {
                            binElem.elementValueID = valuesToID[textNode.Value];
                        }
                    }
                }
                else
                {
                    binElem.childElementStartID = xmlElems.Count;
                    binElem.childElementCount = elem.ChildNodes.Count;

                    xmlElems.AddRange(new BinaryXmlElement[elem.ChildNodes.Count]);
                    int i = binElem.childElementStartID;
                    foreach (XmlElement childElem in elem.ChildNodes)
                    {
                        BuildBinXml(childElem, valuesToID, xmlElems, xmlAttrs, i);
                        i++;
                    }
                }
            }

            // Update the xmlElems Entry
            if (childElemIndex == 0)
            {
                xmlElems[currentIndex] = binElem;
            }
            else
            {
                xmlElems[childElemIndex] = binElem;
            }
        }
    }
}
