#region License
// Copyright (c) 2013 Stichting Centrale Discotheek Rotterdam. All rights reserved.
// 
// website: http://www.muziekweb.nl
// e-mail:  info@cdr.nl
// 
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the
// Free Software Foundation; either version 2 of the License, or 
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful, but 
// WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY
// or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU General Public License
// for more details.
// 
// You should have received a copy of the GNU General Public License along
// with this program; if not, write to the Free Software Foundation, Inc.,
// 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA. or 
// visit www.gnu.org.
#endregion
using System;
using System.Collections.Generic;
using System.Text;

namespace CDR.LibRTMP
{
    public class AMFObject : ICloneable
    {
        private List<AMFObjectProperty> properties = new List<AMFObjectProperty>();

        public AMFObject()
        {
            Reset();
        }

        public object Clone()
        {
            AMFObject clone = new AMFObject();
            foreach (AMFObjectProperty prop in properties)
            {
                clone.properties.Add((AMFObjectProperty)prop.Clone());
            } //foreach

            return clone;
        }

        public int Decode(byte[] pBuffer, int bufferOffset, int size, bool bDecodeName)
        {
            int originalSize = size;
            bool error = false; // if there is an error while decoding - try to at least find the end mark 0x000009

            while (size >= 3)
            {
                if (RTMPHelper.ReadInt24(pBuffer, bufferOffset) == 0x00000009)
                {
                    size -= 3;
                    error = false;
                    break;
                }

                if (error)
                {
                    size--;
                    bufferOffset++;
                    continue;
                }

                AMFObjectProperty prop = new AMFObjectProperty();
                int result = prop.Decode(pBuffer, bufferOffset, size, bDecodeName);
                if (result == -1)
                    error = true;
                else
                {
                    size -= result;
                    bufferOffset += result;
                    properties.Add(prop);
                }
            }

            if (error) return -1;

            return originalSize - size;
        }

        public int DecodeArray(byte[] buffer, int offset, int size, int arrayLength, bool decodeName)
        {
            bool error = false;
            int originalSize = size;

            while (arrayLength > 0)
            {
                arrayLength--;

                AMFObjectProperty prop = new AMFObjectProperty();
                int nRes = prop.Decode(buffer, offset, size, decodeName);
                if (nRes == -1)
                {
                    error = true;
                }
                else
                {
                    size -= nRes;
                    offset += nRes;
                    properties.Add(prop);
                }
            }
            if (error)
            {
                return -1;
            }

            return originalSize - size;
        }

        public void AddProperty(AMFObjectProperty prop)
        {
            properties.Add(prop);
        }

        public int Count
        {
            get
            {
                return properties.Count;
            }
        }

        /// <summary>
        /// Index property for "GetProperty"
        /// </summary>
        /// <returns></returns>
        public AMFObjectProperty this[int index]
        {
            get
            {
                if (index < properties.Count)
                {
                    return properties[index];
                }

                return null;
            }
        }

        /// <summary>
        /// Get property using property name
        /// </summary>
        public AMFObjectProperty GetProperty(string strName)
        {
            for (int n = 0; n < properties.Count; n++)
            {
                if (properties[n].PropertyName == strName) return properties[n];
            } //for

            return null;
        }

        /// <summary>
        /// Get property using index number
        /// </summary>
        public AMFObjectProperty GetProperty(int index)
        {
            if (index < properties.Count)
            {
                return properties[index];
            }

            return null;
        }

        public void FindMatchingProperty(string name, List<AMFObjectProperty> p, int stopAt)
        {
            for (int n = 0; n < properties.Count; n++)
            {
                AMFObjectProperty prop = GetProperty(n);

                if (prop.PropertyName.ToLower() == name.ToLower())
                {
                    if (p == null)
                    {
                        p = new List<AMFObjectProperty>();
                    }
                    p.Add(GetProperty(n));
                    if (p.Count >= stopAt)
                    {
                        return;
                    }
                }

                if (prop.DataType == AMFDataType.AMF_OBJECT)
                {
                    prop.ObjectValue.FindMatchingProperty(name, p, stopAt);
                }
            }            
        }

        public void Dump(LibRTMPLogLevel loglevel = LibRTMPLogLevel.Trace)
        {
            for (int n = 0; n < properties.Count; n++)
            {
                properties[n].Dump(loglevel);
            }
        }

        public void Reset()
        {
            properties.Clear();
        }

        public void Encode(List<byte> output)
        {
            output.Add((byte)AMFDataType.AMF_OBJECT);
            foreach (AMFObjectProperty aProp in properties)
            {
                aProp.Encode(output);
            } //foreach
            RTMPHelper.EncodeInt24(output, (int)AMFDataType.AMF_OBJECT_END);
        }

    }


    public enum AMFDataType
    {
        AMF_NUMBER = 0, AMF_BOOLEAN, AMF_STRING, AMF_OBJECT,
        AMF_MOVIECLIP,		/* reserved */
        AMF_NULL, AMF_UNDEFINED, AMF_REFERENCE, AMF_ECMA_ARRAY, AMF_OBJECT_END,
        AMF_STRICT_ARRAY, AMF_DATE, AMF_LONG_STRING, AMF_UNSUPPORTED,
        AMF_RECORDSET,		/* reserved */
        AMF_XML_DOC, AMF_TYPED_OBJECT,
        AMF_AVMPLUS,		/* use AMF3 */
        AMF_INVALID = 0xff
    };
}
