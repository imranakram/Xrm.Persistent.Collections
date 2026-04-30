namespace Xrm.Persistent.Collections.Backend
{
    using System;
    using Interfaces;

    public static class Extensions
    {
        #region Public Methods

        public static string GenerateKey(this IBlobCache cache) =>
            Guid.NewGuid().ToString("N");

        #endregion Public Methods
    }
}