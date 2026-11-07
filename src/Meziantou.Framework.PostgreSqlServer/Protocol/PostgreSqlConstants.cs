namespace Meziantou.Framework.PostgreSql.Protocol;

internal static class PostgreSqlConstants
{
    public const int ProtocolVersion3 = 196608;
    public const int SslRequestCode = 80877103;
    public const int CancelRequestCode = 80877102;

    /// <summary>Message type tags sent by the client. Several tags collide with <see cref="Backend"/> tags but mean something different.</summary>
    public static class Frontend
    {
        public const byte Query = (byte)'Q';
        public const byte Parse = (byte)'P';
        public const byte Bind = (byte)'B';
        public const byte Describe = (byte)'D';
        public const byte Execute = (byte)'E';
        public const byte Close = (byte)'C';
        public const byte Sync = (byte)'S';
        public const byte Flush = (byte)'H';
        public const byte Terminate = (byte)'X';
        public const byte PasswordMessage = (byte)'p';
    }

    /// <summary>Message type tags sent by the server.</summary>
    public static class Backend
    {
        public const byte Authentication = (byte)'R';
        public const byte ParameterStatus = (byte)'S';
        public const byte BackendKeyData = (byte)'K';
        public const byte ReadyForQuery = (byte)'Z';
        public const byte ParseComplete = (byte)'1';
        public const byte BindComplete = (byte)'2';
        public const byte CloseComplete = (byte)'3';
        public const byte RowDescription = (byte)'T';
        public const byte DataRow = (byte)'D';
        public const byte CommandComplete = (byte)'C';
        public const byte ErrorResponse = (byte)'E';
        public const byte NoticeResponse = (byte)'N';
        public const byte ParameterDescription = (byte)'t';
        public const byte NoData = (byte)'n';
        public const byte EmptyQueryResponse = (byte)'I';
        public const byte PortalSuspended = (byte)'s';
    }

    /// <summary>The target discriminator carried by Describe and Close messages.</summary>
    public static class DescribeTarget
    {
        public const byte Statement = (byte)'S';
        public const byte Portal = (byte)'P';
    }

    /// <summary>Transaction status reported by ReadyForQuery.</summary>
    public static class TransactionStatus
    {
        public const byte Idle = (byte)'I';
        public const byte InTransaction = (byte)'T';
        public const byte Failed = (byte)'E';
    }

    public static class SqlStates
    {
        public const string ProtocolViolation = "08P01";
        public const string InvalidAuthorizationSpecification = "28000";
        public const string InvalidPassword = "28P01";
        public const string InvalidSqlStatementName = "26000";
        public const string QueryCanceled = "57014";
        public const string InternalError = "XX000";
        public const string ConfigurationLimitExceeded = "53400";
        public const string SuccessfulCompletion = "00000";
    }
}
