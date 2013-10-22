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
    public class AMFObjectProperty : ICloneable
    {
        internal string stringName = string.Empty;
        internal AMFDataType type;
        internal double numVal;
        internal AMFObject objVal;
        internal string stringVal;
        internal ushort dateUTCOffset;
        internal double date;

        public AMFObjectProperty()
        {
            Reset();
        }

        /// <summary>
        /// Deep copy!
        /// </summary>
        public object Clone()
        {
            AMFObjectProperty clone = new AMFObjectProperty();
            clone.stringName = stringName;
            clone.type = type;
            clone.numVal = numVal;
            if (objVal != null)
            {
                clone.objVal = (AMFObject)objVal.Clone();
            }
            clone.stringVal = stringVal;
            clone.dateUTCOffset = dateUTCOffset;
            clone.date = date;

            return clone;
        }

        public string PropertyName
        {
            get
            {
                return stringName;
            }
            set
            {
                stringName = value;
            }
        }

        public AMFDataType DataType
        {
            get
            {
                return type;
            }
            set
            {
                type = value;
            }
        }

        public double NumberValue
        {
            get
            {
                return numVal;
            }
            set
            {
                numVal = value;
            }
        }

        public bool BooleanValue
        {
            get
            {
                return numVal != 0;
            }
            set
            {
                numVal = (value) ? 1 : 0;
            }
        }

        public string StringValue
        {
            get
            {
                return stringVal;
            }
            set
            {
                stringVal = value;
            }
        }

        public AMFObject ObjectValue
        {
            get
            {
                return objVal;
            }
            set
            {
                objVal = value;
            }
        }

        public bool IsValid()
        {
            return (type != AMFDataType.AMF_INVALID);
        }

        public int Decode(byte[] buffer, int bufferOffset, int size, bool bDecodeName)
        {
            int originalSize = size;

            if (size == 0 || buffer == null)
            {
                return -1;
            }

            if (buffer[bufferOffset] == 0x05)
            {
                type = AMFDataType.AMF_NULL;
                return 1;
            }

            if (bDecodeName && size < 4) // at least name (length + at least 1 byte) and 1 byte of data
            {
                return -1;
            }

            if (bDecodeName)
            {
                ushort nNameSize = RTMPHelper.ReadInt16(buffer, bufferOffset);
                if (nNameSize > size - (short)sizeof(short))
                {
                    return -1;
                }

                stringName = RTMPHelper.ReadString(buffer, bufferOffset);
                size -= sizeof(short) + stringName.Length;
                bufferOffset += sizeof(short) + stringName.Length;
            }

            if (size == 0)
            {
                return -1;
            }

            size--;

            int stringSize = 0;
            int result = 0;
            switch (buffer[bufferOffset])
            {
                case (byte)AMFDataType.AMF_NUMBER:
                    if (size < (int)sizeof(double))
                    {
                        return -1;
                    }
                    numVal = RTMPHelper.ReadNumber(buffer, bufferOffset + 1);
                    size -= sizeof(double);
                    type = AMFDataType.AMF_NUMBER;
                    break;
                case (byte)AMFDataType.AMF_BOOLEAN:
                    if (size < 1)
                    {
                        return -1;
                    }
                    numVal = Convert.ToDouble(RTMPHelper.ReadBool(buffer, bufferOffset + 1));
                    size--;
                    type = AMFDataType.AMF_BOOLEAN;
                    break;
                case (byte)AMFDataType.AMF_STRING:
                    stringSize = RTMPHelper.ReadInt16(buffer, bufferOffset + 1);
                    if (size < stringSize + (int)sizeof(short))
                    {
                        return -1;
                    }
                    stringVal = RTMPHelper.ReadString(buffer, bufferOffset + 1);
                    size -= sizeof(short) + stringSize;
                    type = AMFDataType.AMF_STRING;
                    break;
                case (byte)AMFDataType.AMF_OBJECT:
                    objVal = new AMFObject();
                    result = objVal.Decode(buffer, bufferOffset + 1, size, true);
                    if (result == -1)
                    {
                        return -1;
                    }
                    size -= result;
                    type = AMFDataType.AMF_OBJECT;
                    break;

                case (byte)AMFDataType.AMF_MOVIECLIP:
                    LibRTMPLogger.Log(LibRTMPLogLevel.Trace, "AMF_MOVIECLIP reserved!");
                    return -1;
                case (byte)AMFDataType.AMF_NULL:
                case (byte)AMFDataType.AMF_UNDEFINED:
                case (byte)AMFDataType.AMF_UNSUPPORTED:
                    type = AMFDataType.AMF_NULL;
                    break;
                case (byte)AMFDataType.AMF_REFERENCE:
                    LibRTMPLogger.Log(LibRTMPLogLevel.Trace, "AMF_REFERENCE not supported!");
                    return -1;
                case (byte)AMFDataType.AMF_ECMA_ARRAY:
                    size -= 4;

                    // next comes the rest, mixed array has a final 0x000009 mark and names, so its an object
                    objVal = new AMFObject();
                    result = objVal.Decode(buffer, bufferOffset + 5, size, true);
                    if (result == -1)
                    {
                        return -1;
                    }
                    size -= result;
                    type = AMFDataType.AMF_OBJECT;
                    break;
                case (byte)AMFDataType.AMF_OBJECT_END:
                    return -1;
                case (byte)AMFDataType.AMF_STRICT_ARRAY:
                    int nArrayLen = RTMPHelper.ReadInt32(buffer, bufferOffset + 1);
                    size -= 4;

                    objVal = new AMFObject();
                    result = objVal.DecodeArray(buffer, bufferOffset + 5, size, nArrayLen, false);
                    if (result == -1)
                    {
                        return -1;
                    }
                    size -= result;
                    type = AMFDataType.AMF_OBJECT;
                    break;
                case (byte)AMFDataType.AMF_DATE:
                    if (size < 10)
                    {
                        return -1;
                    }
                    date = RTMPHelper.ReadNumber(buffer, bufferOffset + 1);
                    dateUTCOffset = RTMPHelper.ReadInt16(buffer, bufferOffset + 9);
                    size -= 10;
                    break;
                case (byte)AMFDataType.AMF_LONG_STRING:
                    stringSize = RTMPHelper.ReadInt32(buffer, bufferOffset + 1);
                    if (size < stringSize + 4)
                    {
                        return -1;
                    }
                    stringVal = RTMPHelper.ReadLongString(buffer, bufferOffset + 1);
                    size -= (4 + stringSize);
                    type = AMFDataType.AMF_STRING;
                    break;
                case (byte)AMFDataType.AMF_RECORDSET:
                    LibRTMPLogger.Log(LibRTMPLogLevel.Trace, "AMFObjectProperty.Decode AMF_RECORDSET reserved!");
                    return -1;
                case (byte)AMFDataType.AMF_XML_DOC:
                    LibRTMPLogger.Log(LibRTMPLogLevel.Trace, "AMFObjectProperty.Decode AMF_XML_DOC not supported!");
                    return -1;
                case (byte)AMFDataType.AMF_TYPED_OBJECT:
                    LibRTMPLogger.Log(LibRTMPLogLevel.Trace, "AMFObjectProperty.Decode AMF_TYPED_OBJECT not supported!");
                    return -1;
                default:
                    LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("AMFObjectProperty.Decode Unknown datatype {0}", buffer[bufferOffset]));
                    return -1;
            } //switch

            return originalSize - size;
        }

        public void Dump(LibRTMPLogLevel loglevel = LibRTMPLogLevel.Trace)
        {
            if (type == AMFDataType.AMF_INVALID)
            {
                LibRTMPLogger.Log(loglevel, "AMFObjectProperty.Dump: Property: INVALID");
                return;
            }

            if (type == AMFDataType.AMF_NULL)
            {
                LibRTMPLogger.Log(loglevel, "AMFObjectProperty.Dump: Property: NULL");
                return;
            }

            if (type == AMFDataType.AMF_OBJECT)
            {
                LibRTMPLogger.Log(loglevel, "AMFObjectProperty.Dump : Property: OBJECT ====>");
                objVal.Dump();
                return;
            }

            string strRes = "no-name. ";
            if (stringName != string.Empty)
            {
                strRes = "Name: " + stringName + ",  ";
            }

            string strVal;

            switch (type)
            {
                case AMFDataType.AMF_NUMBER:
                    strVal = string.Format("NUMBER: {0}", numVal);
                    break;
                case AMFDataType.AMF_BOOLEAN:
                    strVal = string.Format("BOOLEAN: {0}", numVal == 1.0 ? "TRUE" : "FALSE");
                    break;
                case AMFDataType.AMF_STRING:
                    strVal = string.Format("STRING: {0}", stringVal.Length < 256 ? stringVal : "Length: " + stringVal.Length.ToString());
                    break;
                default:
                    strVal = string.Format("INVALID TYPE {0}", type);
                    break;
            } //switch

            strRes += strVal;
            LibRTMPLogger.Log(loglevel, string.Format("Property: {0}", strRes));
        }

        public void Encode(List<byte> output)
        {
            if (type == AMFDataType.AMF_INVALID)
            {
                return;
            }

            switch (type)
            {
                case AMFDataType.AMF_NUMBER:
                    if (string.IsNullOrEmpty(stringName))
                    {
                        RTMPHelper.EncodeNumber(output, NumberValue);
                    }
                    else
                    {
                        RTMPHelper.EncodeNumber(output, stringName, NumberValue);
                    }
                    break;

                case AMFDataType.AMF_BOOLEAN:
                    if (string.IsNullOrEmpty(stringName))
                    {
                        RTMPHelper.EncodeBoolean(output, BooleanValue);
                    }
                    else
                    {
                        RTMPHelper.EncodeBoolean(output, stringName, BooleanValue);
                    }
                    break;

                case AMFDataType.AMF_STRING:
                    if (string.IsNullOrEmpty(stringName))
                    {
                        RTMPHelper.EncodeString(output, StringValue);
                    }
                    else
                    {
                        RTMPHelper.EncodeString(output, stringName, StringValue);
                    }
                    break;

                case AMFDataType.AMF_NULL:
                    output.Add(0x05);
                    break;

                case AMFDataType.AMF_OBJECT:
                    if (!string.IsNullOrEmpty(stringName))
                    {
                        short length = System.Net.IPAddress.HostToNetworkOrder((short)stringName.Length);
                        output.AddRange(BitConverter.GetBytes(length));
                        output.AddRange(Encoding.ASCII.GetBytes(stringName));
                    }
                    objVal.Encode(output);
                    break;

                default:
                    LibRTMPLogger.Log(LibRTMPLogLevel.Trace, string.Format("AMFObjectProperty.Encode invalid type: {0}", type));
                    break;
            } //switch
        }

        public void Reset()
        {
            numVal = 0.0;
            stringVal = "";
            objVal = null;
            type = AMFDataType.AMF_INVALID;
        }
    }
}
