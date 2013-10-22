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
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace CDR.LibRTMP
{
    public static class RTMPHelper
    {
        #region Encode Functions

        public static void EncodeString(List<byte> output, string strName, string strValue)
        {
            short length = IPAddress.HostToNetworkOrder((short)strName.Length);
            output.AddRange(BitConverter.GetBytes(length));
            output.AddRange(Encoding.ASCII.GetBytes(strName));
            EncodeString(output, strValue);
        }

        public static void EncodeString(List<byte> output, string strValue)
        {
            output.Add(0x02); // type: String
            short length = IPAddress.HostToNetworkOrder((short)strValue.Length);
            output.AddRange(BitConverter.GetBytes(length));
            output.AddRange(Encoding.ASCII.GetBytes(strValue));
        }

        public static void EncodeBoolean(List<byte> output, string strName, bool bVal)
        {
            short length = IPAddress.HostToNetworkOrder((short)strName.Length);
            output.AddRange(BitConverter.GetBytes(length));
            output.AddRange(Encoding.ASCII.GetBytes(strName));
            EncodeBoolean(output, bVal);
        }

        public static void EncodeBoolean(List<byte> output, bool bVal)
        {
            output.Add(0x01); // type: Boolean
            output.Add(bVal ? (byte)0x01 : (byte)0x00);
        }

        public static void EncodeNumber(List<byte> output, string strName, double dVal)
        {
            short length = IPAddress.HostToNetworkOrder((short)strName.Length);
            output.AddRange(BitConverter.GetBytes(length));
            output.AddRange(Encoding.ASCII.GetBytes(strName));
            EncodeNumber(output, dVal);
        }

        public static void EncodeNumber(List<byte> output, double dVal)
        {
            output.Add(0x00); // type: Number
            byte[] bytes = BitConverter.GetBytes(dVal);
            for (int i = bytes.Length - 1; i >= 0; i--)
            {
                output.Add(bytes[i]); // add in reversed byte order
            } //for
        }

        public static void EncodeInt16(List<byte> output, short nVal)
        {
            output.AddRange(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(nVal)));
        }

        public static void EncodeInt24(List<byte> output, int nVal)
        {
            byte[] bytes = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(nVal));
            for (int i = 1; i < 4; i++)
            {
                output.Add(bytes[i]);
            } //for
        }

        /// <summary>
        /// big-endian 32bit integer
        /// </summary>
        /// <param name="output"></param>
        /// <param name="nVal"></param>
        public static void EncodeInt32(List<byte> output, int nVal, uint offset = 0)
        {
            byte[] bytes = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(nVal));
            if (offset == 0)
            {
                output.AddRange(bytes);
            }
            else
            {
                for (int i = 0; i < bytes.Length; i++)
                {
                    output[(int)offset + i] = bytes[i];
                } //for
            }
        }

        /// <summary>
        /// little-endian 32bit integer
        /// TODO: this is wrong on big-endian processors
        /// </summary>
        /// <param name="output"></param>
        /// <param name="nVal"></param>
        public static void EncodeInt32LE(List<byte> output, int nVal)
        {
            output.AddRange(BitConverter.GetBytes(nVal));
        }

        #endregion

        #region Read Functions
        
        public static string ReadString(byte[] data, int offset)
        {
            string strRes = "";
            ushort length = ReadInt16(data, offset);
            if (length > 0)
            {
                strRes = Encoding.ASCII.GetString(data, offset + 2, length);
            }
            return strRes;
        }

        public static string ReadLongString(byte[] data, int offset)
        {
            string strRes = "";
            int length = ReadInt32(data, offset);
            if (length > 0)
            {
                strRes = Encoding.ASCII.GetString(data, offset + 4, length);
            }

            return strRes;
        }

        public static ushort ReadInt16(byte[] data, int offset)
        {
            return (ushort)((data[offset] << 8) | data[offset + 1]);
        }

        public static int ReadInt24(byte[] data, int offset)
        {
            return (data[offset] << 16) | (data[offset + 1] << 8) | data[offset + 2];
        }

        /// <summary>
        /// big-endian 32bit integer
        /// </summary>
        /// <param name="data"></param>
        /// <param name="offset"></param>
        /// <returns></returns>
        public static int ReadInt32(byte[] data, int offset)
        {
            return (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
        }

        /// <summary>
        /// little-endian 32bit integer
        /// TODO: this is wrong on big-endian processors
        /// </summary>
        /// <param name="data"></param>
        /// <param name="offset"></param>
        /// <returns></returns>
        public static int ReadInt32LE(byte[] data, int offset)
        {
            return BitConverter.ToInt32(data, offset);
        }

        public static bool ReadBool(byte[] data, int offset)
        {
            return data[offset] == 0x01;
        }

        public static double ReadNumber(byte[] data, int offset)
        {
            byte[] bytes = new byte[8];
            Array.Copy(data, offset, bytes, 0, 8);
            Array.Reverse(bytes); // reversed byte order
            return BitConverter.ToDouble(bytes, 0);
        }

        #endregion

        #region RTMPE helper functions for encryption

        public static uint GetDHOffset(int alg, byte[] handshake, int bufferoffset, uint len)
        {
            if (alg == 0)
            {
                return GetDHOffset1(handshake, bufferoffset, len);
            }
            else
            {
                return GetDHOffset2(handshake, bufferoffset, len);
            }
        }

        public static uint GetDHOffset1(byte[] handshake, int bufferoffset, uint len)
        {
            int offset = 0;
            bufferoffset += 1532;

            offset += handshake[bufferoffset]; bufferoffset++;
            offset += handshake[bufferoffset]; bufferoffset++;
            offset += handshake[bufferoffset]; bufferoffset++;
            offset += handshake[bufferoffset];// (*ptr);

            int res = (offset % 632) + 772;

            if (res + 128 > 1531)
            {
                string msg = string.Format("[CDR.LibRTMP.RTMPHelper] Couldn't calculate DH offset (got {0}), exiting!", res);
                LibRTMPLogger.Log(LibRTMPLogLevel.Warning, msg);
                throw new Exception(msg);
            }

            return (uint)res;
        }

        public static uint GetDHOffset2(byte[] handshake, int bufferoffset, uint len)
        {
            uint offset = 0;
            bufferoffset += 768;

            offset += handshake[bufferoffset]; bufferoffset++;
            offset += handshake[bufferoffset]; bufferoffset++;
            offset += handshake[bufferoffset]; bufferoffset++;
            offset += handshake[bufferoffset];

            uint res = (offset % 632) + 8;

            if (res + 128 > 767)
            {
                string msg = string.Format("[CDR.LibRTMP.RTMPHelper] Couldn't calculate correct DH offset (got {0}), exiting!", res);
                LibRTMPLogger.Log(LibRTMPLogLevel.Warning, msg);
                throw new Exception(msg);
            }
            return res;
        }

        public static uint GetDigestOffset(int alg, byte[] handshake, int bufferoffset, uint len)
        {
            if (alg == 0)
            {
                return GetDigestOffset1(handshake, bufferoffset, len);
            }
            else
            {
                return GetDigestOffset2(handshake, bufferoffset, len);
            }
        }

        public static uint GetDigestOffset1(byte[] handshake, int bufferoffset, uint len)
        {
            int offset = 0;
            bufferoffset += 8;

            offset += handshake[bufferoffset]; bufferoffset++;
            offset += handshake[bufferoffset]; bufferoffset++;
            offset += handshake[bufferoffset]; bufferoffset++;
            offset += handshake[bufferoffset];

            int res = (offset % 728) + 12;

            if (res + 32 > 771)
            {
                string msg = string.Format("[CDR.LibRTMP.RTMPHelper] Couldn't calculate digest offset (got {0}), exiting!", res);
                LibRTMPLogger.Log(LibRTMPLogLevel.Warning, msg);
                throw new Exception(msg);
            }

            return (uint)res;
        }

        public static uint GetDigestOffset2(byte[] handshake, int bufferoffset, uint len)
        {
            uint offset = 0;
            bufferoffset += 772;
            //assert(12 <= len);

            offset += handshake[bufferoffset]; bufferoffset++;
            offset += handshake[bufferoffset]; bufferoffset++;
            offset += handshake[bufferoffset]; bufferoffset++;
            offset += handshake[bufferoffset];// (*ptr);

            uint res = (offset % 728) + 776;

            if (res + 32 > 1535)
            {
                string msg = string.Format("[CDR.LibRTMP.RTMPHelper] Couldn't calculate correct digest offset (got {0}), exiting", res);
                LibRTMPLogger.Log(LibRTMPLogLevel.Warning, msg);
                throw new Exception(msg);
            }
            return res;
        }

        public static void CalculateDigest(int digestPos, byte[] handshakeMessage, int handshakeOffset, byte[] key, int keyLen, byte[] digest, int digestOffset)
        {
            const int messageLen = RTMPConst.RTMP_SIG_SIZE - RTMPConst.SHA256_DIGEST_LENGTH;
            byte[] message = new byte[messageLen];

            Array.Copy(handshakeMessage, handshakeOffset, message, 0, digestPos);
            Array.Copy(handshakeMessage, handshakeOffset + digestPos + RTMPConst.SHA256_DIGEST_LENGTH, message, digestPos, messageLen - digestPos);

            HMACsha256(message, 0, messageLen, key, keyLen, digest, digestOffset);
        }

        public static bool VerifyDigest(int digestPos, byte[] handshakeMessage, byte[] key, int keyLen)
        {
            byte[] calcDigest = new byte[RTMPConst.SHA256_DIGEST_LENGTH];

            CalculateDigest(digestPos, handshakeMessage, 0, key, keyLen, calcDigest, 0);

            for (int i = 0; i < RTMPConst.SHA256_DIGEST_LENGTH; i++)
            {
                if (handshakeMessage[digestPos + i] != calcDigest[i])
                {
                    return false;
                }
            } //for

            return true;
        }

        public static void HMACsha256(byte[] message, int messageOffset, int messageLen, byte[] key, int keylen, byte[] digest, int digestOffset)
        {
            System.Security.Cryptography.HMAC hmac = System.Security.Cryptography.HMACSHA256.Create("HMACSHA256");
            byte[] actualKey = new byte[keylen]; Array.Copy(key, actualKey, keylen);
            hmac.Key = actualKey;

            byte[] actualMessage = new byte[messageLen];
            Array.Copy(message, messageOffset, actualMessage, 0, messageLen);

            byte[] calcDigest = hmac.ComputeHash(actualMessage);
            Array.Copy(calcDigest, 0, digest, digestOffset, calcDigest.Length);
        }

        public static void InitRC4Encryption(byte[] secretKey, byte[] pubKeyIn, int inOffset, byte[] pubKeyOut, int outOffset, out byte[] rc4keyIn, out byte[] rc4keyOut)
        {
            byte[] digest = new byte[RTMPConst.SHA256_DIGEST_LENGTH];

            System.Security.Cryptography.HMAC hmac = System.Security.Cryptography.HMACSHA256.Create("HMACSHA256");
            hmac.Key = secretKey;

            byte[] actualpubKeyIn = new byte[128];
            Array.Copy(pubKeyIn, inOffset, actualpubKeyIn, 0, 128);
            digest = hmac.ComputeHash(actualpubKeyIn);

            rc4keyOut = new byte[16];
            Array.Copy(digest, rc4keyOut, 16);
            LibRTMPLogger.Log(LibRTMPLogLevel.Trace, "[CDR.LibRTMP.RTMPHelper] RC4 Out Key: ");
            LibRTMPLogger.LogHex(LibRTMPLogLevel.Trace, rc4keyOut, 0, 16);

            hmac = System.Security.Cryptography.HMACSHA256.Create("HMACSHA256");
            hmac.Key = secretKey;

            byte[] actualpubKeyOut = new byte[128];
            Array.Copy(pubKeyOut, outOffset, actualpubKeyOut, 0, 128);
            digest = hmac.ComputeHash(actualpubKeyOut);

            rc4keyIn = new byte[16];
            Array.Copy(digest, rc4keyIn, 16);
            LibRTMPLogger.Log(LibRTMPLogLevel.Trace, "[CDR.LibRTMP.RTMPHelper] RC4 In Key: ");
            LibRTMPLogger.LogHex(LibRTMPLogLevel.Trace, rc4keyIn, 0, 16);
        }

        /// <summary>
        /// RTMPE type 8 uses XTEA on the regular signature ("http://en.wikipedia.org/wiki/XTEA")
        /// </summary>
        public static void rtmpe8_sig(byte[] array, int offset, int keyid)
        {
            uint i, num_rounds = 32;
            uint v0, v1, sum = 0, delta = 0x9E3779B9;
            uint[] k;

            v0 = BitConverter.ToUInt32(array, offset);
            v1 = BitConverter.ToUInt32(array, offset + 4);

            k = RTMPConst.rtmpe8_keys[keyid];

            for (i = 0; i < num_rounds; i++)
            {
                v0 += (((v1 << 4) ^ (v1 >> 5)) + v1) ^ (sum + k[sum & 3]);
                sum += delta;
                v1 += (((v0 << 4) ^ (v0 >> 5)) + v0) ^ (sum + k[(sum >> 11) & 3]);
            }

            Array.Copy(BitConverter.GetBytes(v0), 0, array, offset, 4);
            Array.Copy(BitConverter.GetBytes(v1), 0, array, offset + 4, 4);
        }

        /// <summary>
        /// RTMPE type 9 uses Blowfish on the regular signature ("http://en.wikipedia.org/wiki/Blowfish_(cipher)")
        /// </summary>
        public static void rtmpe9_sig(byte[] array, int offset, int keyid)
        {
            Org.BouncyCastle.Crypto.Engines.BlowfishEngine bf = new Org.BouncyCastle.Crypto.Engines.BlowfishEngine();
            bf.LittleEndian = true;
            bf.Init(true, new Org.BouncyCastle.Crypto.Parameters.KeyParameter(RTMPConst.rtmpe9_keys[keyid]));
            byte[] output = new byte[8];
            bf.ProcessBlock(array, offset, output, 0);
            Array.Copy(output, 0, array, offset, 8);
        }

        /// <summary>
        /// Check is the key is valid see RFC 2631, Section 2.1.5, http://www.ietf.org/rfc/rfc2631.txt
        /// </summary>
        public static bool IsValidPublicKey(Org.BouncyCastle.Math.BigInteger y, Org.BouncyCastle.Math.BigInteger p, Org.BouncyCastle.Math.BigInteger q)
        {
            Org.BouncyCastle.Math.BigInteger bn;

            // y must lie in [2,p-1]
            // check y < 2 then failed
            bn = new Org.BouncyCastle.Math.BigInteger("2");
            if (y.CompareTo(bn) < 0)
            {
                LibRTMPLogger.Log(LibRTMPLogLevel.Warning, "[CDR.LibRTMP.RTMPHelper.IsValidPublicKey] DH public key must be at least 2");
                return false;
            }

            // y must lie in [2,p-1]
            bn = new Org.BouncyCastle.Math.BigInteger(p.ToString());
            bn = bn.Subtract(new Org.BouncyCastle.Math.BigInteger("1"));
            if (y.CompareTo(bn) > 0)
            {
                LibRTMPLogger.Log(LibRTMPLogLevel.Warning, "[CDR.LibRTMP.RTMPHelper.IsValidPublicKey] DH public key must be at most p-2");
                return false;
            }


            // Verify with Sophie-Germain prime
            //
            // This is a nice test to make sure the public key position is calculated
            // correctly. This test will fail in about 50% of the cases if applied to
            // random data.
            bn = y.ModPow(q, p);
            if (bn.CompareTo(new Org.BouncyCastle.Math.BigInteger("1")) != 0)
            {
                LibRTMPLogger.Log(LibRTMPLogLevel.Warning, "[CDR.LibRTMP.RTMPHelper.IsValidPublicKey] DH public key does not fulfill y^q mod p = 1");
                return false;
            }

            return true;
        }

        #endregion



        static readonly int[] Empty = new int[0];

        public static int[] Locate(this byte[] self, byte[] candidate)
        {
            if (IsEmptyLocate(self, candidate))
                return Empty;

            var list = new List<int>();

            for (int i = 0; i < self.Length; i++)
            {
                if (!IsMatch(self, i, candidate))
                    continue;

                list.Add(i);
            }

            return list.Count == 0 ? Empty : list.ToArray();
        }

        static bool IsMatch(byte[] array, int position, byte[] candidate)
        {
            if (candidate.Length > (array.Length - position))
                return false;

            for (int i = 0; i < candidate.Length; i++)
                if (array[position + i] != candidate[i])
                    return false;

            return true;
        }

        static bool IsEmptyLocate(byte[] array, byte[] candidate)
        {
            return array == null
                || candidate == null
                || array.Length == 0
                || candidate.Length == 0
                || candidate.Length > array.Length;
        }


    }


}
