using System;
using System.Collections.Generic;
using System.Text;

namespace HikCodeReader
{
    /// <summary>
    /// HikCodeReader SDK异常
    /// </summary>
    public class MvCodeReaderException : Exception
    {
        public int ErrorCode { get; }

        /// <summary>
        /// 使用指定的错误消息和错误码初始化 MvCodeReaderException 类的新实例
        /// </summary>
        /// <param name="message">描述错误的消息</param>
        /// <param name="errorCode">SDK错误码</param>
        public MvCodeReaderException(string message, int errorCode) : base(message)
        {
            ErrorCode = errorCode;
        }

        /// <summary>
        /// 使用指定的错误消息、错误码和内部异常初始化 MvCodeReaderException 类的新实例
        /// </summary>
        /// <param name="message">描述错误的消息</param>
        /// <param name="errorCode">SDK错误码</param>
        /// <param name="innerException">导致当前异常的异常</param>
        public MvCodeReaderException(string message, int errorCode, Exception innerException) : base(message, innerException)
        {
            ErrorCode = errorCode;
        }
    }
}
