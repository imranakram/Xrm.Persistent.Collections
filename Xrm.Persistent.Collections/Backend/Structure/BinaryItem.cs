namespace Xrm.Persistent.Collections.Backend.Structure
{
    internal class BinaryItem
    {
        #region Public Constructors

        public BinaryItem()
        {
        }

        public BinaryItem(byte[] data)
        {
            Data = data;
        }

        #endregion Public Constructors

        #region Public Properties

        public byte[] Data
        {
            get;
            set;
        }

        #endregion Public Properties
    }
}