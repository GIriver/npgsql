﻿using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Npgsql.BackendMessages;
using Npgsql.PostgresTypes;
using Npgsql.TypeHandling;
using Npgsql.TypeMapping;
using NpgsqlTypes;

#pragma warning disable 618
namespace Npgsql.TypeHandlers.NetworkHandlers
{
    /// <remarks>
    /// http://www.postgresql.org/docs/current/static/datatype-net-types.html
    /// </remarks>
    [TypeMapping(
        "inet",
        NpgsqlDbType.Inet,
        new[] { typeof(IPAddress), typeof((IPAddress Address, int Subnet)), typeof(NpgsqlInet) })]
    class InetHandler : NpgsqlSimpleTypeHandlerWithPsv<IPAddress, (IPAddress Address, int Subnet)>,
        INpgsqlSimpleTypeHandler<NpgsqlInet>
    {
        // ReSharper disable InconsistentNaming
        const byte IPv4 = 2;
        const byte IPv6 = 3;
        // ReSharper restore InconsistentNaming

        public InetHandler(PostgresType postgresType) : base(postgresType) {}

        #region Read

        public override IPAddress Read(NpgsqlReadBuffer buf, int len, FieldDescription? fieldDescription = null)
            => DoRead(buf, len, fieldDescription, false).Address;

#pragma warning disable CA1801 // Review unused parameters
        internal static (IPAddress Address, int Subnet) DoRead(
            NpgsqlReadBuffer buf,
            int len,
            FieldDescription? fieldDescription,
            bool isCidrHandler)
        {
            buf.ReadByte(); // addressFamily
            var mask = buf.ReadByte();
            var isCidr = buf.ReadByte() == 1;
            Debug.Assert(isCidrHandler == isCidr);
            var numBytes = buf.ReadByte();
            var bytes = new byte[numBytes];
            for (var i = 0; i < numBytes; i++)
                bytes[i] = buf.ReadByte();

            return (new IPAddress(bytes), mask);
        }
#pragma warning restore CA1801 // Review unused parameters

        protected override (IPAddress Address, int Subnet) ReadPsv(NpgsqlReadBuffer buf, int len, FieldDescription? fieldDescription = null)
            => DoRead(buf, len, fieldDescription, false);

        NpgsqlInet INpgsqlSimpleTypeHandler<NpgsqlInet>.Read(NpgsqlReadBuffer buf, int len, FieldDescription? fieldDescription)
        {
            var (address, subnet) = DoRead(buf, len, fieldDescription, false);
            return new NpgsqlInet(address, subnet);
        }

        #endregion Read

        #region Write

        protected internal override int ValidateObjectAndGetLength(object value, ref NpgsqlLengthCache? lengthCache, NpgsqlParameter? parameter)
            => value switch {
                null => -1,
                DBNull _ => -1,
                IPAddress ip => ValidateAndGetLength(ip, parameter),
                ValueTuple<IPAddress, int> tup => ValidateAndGetLength(tup, parameter),
                NpgsqlInet inet => ValidateAndGetLength(inet, parameter),
                _ => throw new InvalidCastException($"Can't write CLR type {value.GetType().Name} to database type {PgDisplayName}")
            };

        protected internal override Task WriteObjectWithLength(object value, NpgsqlWriteBuffer buf, NpgsqlLengthCache? lengthCache, NpgsqlParameter? parameter, bool async)
            => value switch {
                DBNull _ => WriteWithLengthInternal(DBNull.Value, buf, lengthCache, parameter, async),
                IPAddress ip => WriteWithLengthInternal(ip, buf, lengthCache, parameter, async),
                ValueTuple<IPAddress, int> tup => WriteWithLengthInternal(tup, buf, lengthCache, parameter, async),
                NpgsqlInet inet => WriteWithLengthInternal(inet, buf, lengthCache, parameter, async),
                _ => throw new InvalidCastException($"Can't write CLR type {value.GetType().Name} to database type {PgDisplayName}")
            };

        public override int ValidateAndGetLength(IPAddress value, NpgsqlParameter? parameter)
            => GetLength(value);

        public override int ValidateAndGetLength((IPAddress Address, int Subnet) value, NpgsqlParameter? parameter)
            => GetLength(value.Address);

        public int ValidateAndGetLength(NpgsqlInet value, NpgsqlParameter? parameter)
            => GetLength(value.Address);

        public override void Write(IPAddress value, NpgsqlWriteBuffer buf, NpgsqlParameter? parameter)
            => DoWrite(value, -1, buf, false);

        public override void Write((IPAddress Address, int Subnet) value, NpgsqlWriteBuffer buf, NpgsqlParameter? parameter)
            => DoWrite(value.Address, value.Subnet, buf, false);

        public void Write(NpgsqlInet value, NpgsqlWriteBuffer buf, NpgsqlParameter? parameter)
            => DoWrite(value.Address, value.Netmask, buf, false);

        internal static void DoWrite(IPAddress ip, int mask, NpgsqlWriteBuffer buf, bool isCidrHandler)
        {
            switch (ip.AddressFamily) {
            case AddressFamily.InterNetwork:
                buf.WriteByte(IPv4);
                if (mask == -1)
                    mask = 32;
                break;
            case AddressFamily.InterNetworkV6:
                buf.WriteByte(IPv6);
                if (mask == -1)
                    mask = 128;
                break;
            default:
                throw new InvalidCastException($"Can't handle IPAddress with AddressFamily {ip.AddressFamily}, only InterNetwork or InterNetworkV6!");
            }

            buf.WriteByte((byte)mask);
            buf.WriteByte((byte)(isCidrHandler ? 1 : 0));  // Ignored on server side
            var bytes = ip.GetAddressBytes();
            buf.WriteByte((byte)bytes.Length);
            buf.WriteBytes(bytes, 0, bytes.Length);
        }

        internal static int GetLength(IPAddress value)
        {
            switch (value.AddressFamily)
            {
            case AddressFamily.InterNetwork:
                return 8;
            case AddressFamily.InterNetworkV6:
                return 20;
            default:
                throw new InvalidCastException($"Can't handle IPAddress with AddressFamily {value.AddressFamily}, only InterNetwork or InterNetworkV6!");
            }
        }

        #endregion Write
    }
}
