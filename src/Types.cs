using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IDADiffCalculator
{
    /// <summary>
    /// Implement the offset or address.
    /// </summary>
    /// <seealso cref="System.IComparable" />
    /// <seealso cref="System.IComparable{RETools.Core.Offset}" />
    public struct Offset : IComparable, IComparable<Offset>
    {
        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="Offset"/> struct.
        /// </summary>
        /// <param name="value">The value.</param>
        public Offset(int value)
        {
            this.Value = value;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Offset"/> struct.
        /// </summary>
        /// <param name="value">The value.</param>
        public Offset(long value)
        {
            this.Value = value;
        }

        #endregion

        #region Offset members

        /// <summary>
        /// Gets or sets the value.
        /// </summary>
        /// <value>
        /// The value.
        /// </value>
        private long Value
        {
            get;
            set;
        }

        /// <summary>
        /// Convert offset to signed long value.
        /// </summary>
        /// <returns></returns>
        public long ToInt64()
        {
            return this.Value;
        }

        /// <summary>
        /// Convert offset to signed int value.
        /// </summary>
        /// <returns></returns>
        public int ToInt32()
        {
            ulong nx = unchecked((ulong)this.Value);
            uint ux = (uint)(nx & 0xFFFFFFFF);
            return unchecked((int)ux);
        }

        /// <summary>
        /// Tries to parse text to offset.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <param name="result">The result.</param>
        /// <returns></returns>
        /// <exception cref="System.ArgumentOutOfRangeException">text;Can't parse offset from empty string!</exception>
        public static bool TryParse(string text, out Offset result)
        {
            if (string.IsNullOrEmpty(text))
            {
                result = new Offset();
                return false;
            }

            long ux = 0;
            if (Utility.TryParseInt64(text, out ux))
            {
                result = ux;
                return true;
            }

            result = new Offset();
            return false;
        }

        /// <summary>
        /// Parses the specified text to offset.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <returns></returns>
        /// <exception cref="System.ArgumentOutOfRangeException">text;Can't parse empty string to offset!</exception>
        public Offset Parse(string text)
        {
            if (string.IsNullOrEmpty(text))
                throw new ArgumentOutOfRangeException("text", "Can't parse empty string to offset!");

            Offset r;
            if (!TryParse(text, out r))
                throw new FormatException("Unable to parse text to offset!");

            return r;
        }

        #endregion

        #region Object overloads

        /// <summary>
        /// Returns a hash code for this instance.
        /// </summary>
        /// <returns>
        /// A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table. 
        /// </returns>
        public override int GetHashCode()
        {
            return this.Value.GetHashCode();
        }

        /// <summary>
        /// Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="System.String" /> that represents this instance.
        /// </returns>
        public override string ToString()
        {
            if (this.Value >= 0)
            {
                if (this.Value < 10)
                    return this.Value.ToString();

                return "0x" + this.Value.ToString("X", Utility.Culture);
            }

            if (this.Value >= -10000)
                return this.Value.ToString();

            return "0x" + this.Value.ToString("X", Utility.Culture);
        }

        /// <summary>
        /// Determines whether the specified <see cref="System.Object" />, is equal to this instance.
        /// </summary>
        /// <param name="obj">The <see cref="System.Object" /> to compare with this instance.</param>
        /// <returns>
        ///   <c>true</c> if the specified <see cref="System.Object" /> is equal to this instance; otherwise, <c>false</c>.
        /// </returns>
        public override bool Equals(object obj)
        {
            if (!(obj is Offset))
                return false;

            return this.Value == ((Offset)obj).Value;
        }

        #endregion

        #region Operator overloads

        /// <summary>
        /// Performs an implicit conversion from <see cref="System.Int64"/> to <see cref="Offset"/>.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>
        /// The result of the conversion.
        /// </returns>
        public static implicit operator Offset(long value)
        {
            return new Offset(value);
        }

        /// <summary>
        /// Implements the operator +.
        /// </summary>
        /// <param name="first">The first.</param>
        /// <param name="second">The second.</param>
        /// <returns>
        /// The result of the operator.
        /// </returns>
        public static Offset operator +(Offset first, Offset second)
        {
            return new Offset(unchecked(first.Value + second.Value));
        }

        /// <summary>
        /// Implements the operator -.
        /// </summary>
        /// <param name="first">The first.</param>
        /// <param name="second">The second.</param>
        /// <returns>
        /// The result of the operator.
        /// </returns>
        public static Offset operator -(Offset first, Offset second)
        {
            return new Offset(unchecked(first.Value - second.Value));
        }

        /// <summary>
        /// Implements the operator *.
        /// </summary>
        /// <param name="first">The first.</param>
        /// <param name="second">The second.</param>
        /// <returns>
        /// The result of the operator.
        /// </returns>
        public static Offset operator *(Offset first, Offset second)
        {
            return new Offset(unchecked(first.Value * second.Value));
        }

        /// <summary>
        /// Implements the operator /.
        /// </summary>
        /// <param name="first">The first.</param>
        /// <param name="second">The second.</param>
        /// <returns>
        /// The result of the operator.
        /// </returns>
        /// <exception cref="System.DivideByZeroException">Trying to divide by zero offset!</exception>
        public static Offset operator /(Offset first, Offset second)
        {
            if (second.Value == 0)
                throw new DivideByZeroException("Trying to divide by zero offset!");
            return new Offset(unchecked(first.Value / second.Value));
        }

        /// <summary>
        /// Implements the operator ==.
        /// </summary>
        /// <param name="first">The first.</param>
        /// <param name="second">The second.</param>
        /// <returns>
        /// The result of the operator.
        /// </returns>
        public static bool operator ==(Offset first, Offset second)
        {
            return first.Value == second.Value;
        }

        /// <summary>
        /// Implements the operator !=.
        /// </summary>
        /// <param name="first">The first.</param>
        /// <param name="second">The second.</param>
        /// <returns>
        /// The result of the operator.
        /// </returns>
        public static bool operator !=(Offset first, Offset second)
        {
            return first.Value != second.Value;
        }

        /// <summary>
        /// Implements the operator &lt;.
        /// </summary>
        /// <param name="first">The first.</param>
        /// <param name="second">The second.</param>
        /// <returns>
        /// The result of the operator.
        /// </returns>
        public static bool operator <(Offset first, Offset second)
        {
            return first.Value < second.Value;
        }

        /// <summary>
        /// Implements the operator &gt;.
        /// </summary>
        /// <param name="first">The first.</param>
        /// <param name="second">The second.</param>
        /// <returns>
        /// The result of the operator.
        /// </returns>
        public static bool operator >(Offset first, Offset second)
        {
            return first.Value > second.Value;
        }

        /// <summary>
        /// Implements the operator &lt;=.
        /// </summary>
        /// <param name="first">The first.</param>
        /// <param name="second">The second.</param>
        /// <returns>
        /// The result of the operator.
        /// </returns>
        public static bool operator <=(Offset first, Offset second)
        {
            return first.Value <= second.Value;
        }

        /// <summary>
        /// Implements the operator &gt;=.
        /// </summary>
        /// <param name="first">The first.</param>
        /// <param name="second">The second.</param>
        /// <returns>
        /// The result of the operator.
        /// </returns>
        public static bool operator >=(Offset first, Offset second)
        {
            return first.Value >= second.Value;
        }

        #endregion

        #region IComparable<Offset> interface

        /// <summary>
        /// Compares the current object with another object of the same type.
        /// </summary>
        /// <param name="other">An object to compare with this object.</param>
        /// <returns>
        /// A value that indicates the relative order of the objects being compared. The return value has the following meanings: Value Meaning Less than zero This object is less than the <paramref name="other" /> parameter.Zero This object is equal to <paramref name="other" />. Greater than zero This object is greater than <paramref name="other" />.
        /// </returns>
        public int CompareTo(Offset other)
        {
            return this.Value.CompareTo(other.Value);
        }

        #endregion

        #region IComparable interface

        /// <summary>
        /// Compares the current instance with another object of the same type and returns an integer that indicates whether the current instance precedes, follows, or occurs in the same position in the sort order as the other object.
        /// </summary>
        /// <param name="obj">An object to compare with this instance.</param>
        /// <returns>
        /// A value that indicates the relative order of the objects being compared. The return value has these meanings: Value Meaning Less than zero This instance precedes <paramref name="obj" /> in the sort order. Zero This instance occurs in the same position in the sort order as <paramref name="obj" />. Greater than zero This instance follows <paramref name="obj" /> in the sort order.
        /// </returns>
        public int CompareTo(object obj)
        {
            if (!(obj is Offset))
                return 0;

            return this.Value.CompareTo(((Offset)obj).Value);
        }

        #endregion
    }

    /// <summary>
    /// List of architecture types.
    /// </summary>
    public enum ArchitectureTypes : byte
    {
        /// <summary>
        /// 32-bit.
        /// </summary>
        x86_32 = 0,

        /// <summary>
        /// 64-bit.
        /// </summary>
        x86_64 = 1,
    }

    /// <summary>
    /// Class used to track progress of something. Thread safe.
    /// </summary>
    public class Progress
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Progress"/> class.
        /// </summary>
        public Progress()
        {

        }

        private long _current = 0;
        private long _total = 0;
        private long _init = 0;

        /// <summary>
        /// Gets the percent.
        /// </summary>
        /// <value>
        /// The percent.
        /// </value>
        public int Percent
        {
            get
            {
                long i = Interlocked.Read(ref _init);
                if (i == 0)
                    return 0;

                long cur = Interlocked.Read(ref _current);
                long max = Interlocked.Read(ref _total);
                if (max <= 0)
                    return 100;
                if (cur <= 0)
                    return 0;

                long pct = cur * 100 / max;
                if (pct <= 0)
                    return 0;
                if (pct >= 100)
                    return 100;
                return (int)pct;
            }
        }

        /// <summary>
        /// Initializes the progress counter.
        /// </summary>
        /// <param name="current">The current done.</param>
        /// <param name="total">The total amount.</param>
        public void Initialize(long total, long current = 0)
        {
            Interlocked.Exchange(ref _total, total);
            Interlocked.Exchange(ref _current, current);
            Interlocked.Exchange(ref _init, 1);
        }

        /// <summary>
        /// Advances the progress counter by specified amount.
        /// </summary>
        /// <param name="count">The count.</param>
        public void Advance(long count = 1)
        {
            if (count == 0)
                return;

            Interlocked.Add(ref _current, count);
        }

        /// <summary>
        /// Sets the progress.
        /// </summary>
        /// <param name="amount">The amount done.</param>
        public void SetProgress(long amount)
        {
            Interlocked.Exchange(ref _current, amount);
        }
    }

    /// <summary>
    /// Some utility functions.
    /// </summary>
    public static class Utility
    {
        /// <summary>
        /// Gets or sets the culture used.
        /// </summary>
        /// <value>
        /// The culture.
        /// </value>
        public static System.Globalization.CultureInfo Culture
        {
            get;
            set;
        } = System.Globalization.CultureInfo.InvariantCulture;

        /// <summary>
        /// Determines whether the specified text is int64.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <returns></returns>
        public static bool IsInt64(string text)
        {
            long v = 0;
            return TryParseInt64(text, out v);
        }

        /// <summary>
        /// Tries to parse int64 list.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <param name="result">The result.</param>
        /// <returns></returns>
        public static bool TryParseInt64List(string text, out List<long> result)
        {
            result = null;

            text = (text ?? "").Trim();
            if (text.Length == 0)
            {
                result = new List<long>(0);
                return true;
            }

            var spl = text.Split(new[] { ' ', ',', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var ls = new List<long>(spl.Length);
            for (int i = 0; i < spl.Length; i++)
            {
                long nx = 0;
                if (!TryParseInt64(spl[i], out nx))
                    return false;

                ls.Add(nx);
            }

            result = ls;
            return true;
        }

        /// <summary>
        /// Calculates the hash code of bytes.
        /// </summary>
        /// <param name="data">The data.</param>
        /// <returns></returns>
        /// <exception cref="System.ArgumentNullException">data</exception>
        public static ulong Calculate64BitHashCode(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException("data");

            using (var algorithm = new XXHash64())
            {
                algorithm.ComputeHash(data);
                return algorithm.HashUInt64;
            }
        }

        /// <summary>
        /// Calculates the hash code of list.
        /// </summary>
        /// <param name="data">The data.</param>
        /// <returns></returns>
        public static ulong Calculate64BitHashCode(IReadOnlyList<ulong> data)
        {
            if (data.Count == 0)
                return 0;

            using (var ms = new System.IO.MemoryStream(data.Count * 8))
            {
                using (var wr = new System.IO.BinaryWriter(ms))
                {
                    foreach (var u in data)
                        wr.Write(u);

                    return Calculate64BitHashCode(ms.ToArray());
                }
            }
        }

        /// <summary>
        /// Calculates the hash code of text.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <returns></returns>
        /// <exception cref="System.ArgumentNullException">text</exception>
        public static ulong Calculate64BitHashCode(string text)
        {
            if (text == null)
                throw new ArgumentNullException("text");

            byte[] data = Encoding.UTF8.GetBytes(text);
            return Calculate64BitHashCode(data);
        }

        /// <summary>
        /// Parses the int64. This expect exactly correct input format or it will throw exception.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <param name="hex">if set to <c>true</c> then text is hexadecimal but without the prefix!</param>
        /// <returns></returns>
        /// <exception cref="System.FormatException">
        /// </exception>
        public static long ParseInt64ExactFast(string text, bool hex)
        {
            if (hex)
            {
                long num;
                char ch = text[0];
                if (ch >= '0' && ch <= '9')
                    num = (int)(ch - '0');
                else if (ch >= 'a' && ch <= 'f')
                    num = (int)ch - 87;
                else if (ch >= 'A' && ch <= 'F')
                    num = (int)ch - 55;
                else
                    throw new FormatException();

                int index = 1;
                int len = text.Length;
                while (index < len)
                {
                    ch = text[index++];
                    if (ch >= '0' && ch <= '9')
                        num = num * 16 + (int)(ch - '0');
                    else if (ch >= 'a' && ch <= 'f')
                        num = num * 16 + ((int)ch - 87);
                    else if (ch >= 'A' && ch <= 'F')
                        num = num * 16 + ((int)ch - 55);
                    else
                        throw new FormatException();
                }
                return num;
            }
            else
            {
                long num;
                bool neg = false;
                char ch = text[0];
                int index = 1;
                if (ch == '-')
                {
                    neg = true;
                    index = 2;

                    ch = text[1];
                    if (ch >= '0' && ch <= '9')
                        num = (int)(ch - '0');
                    else
                        throw new FormatException();
                }
                else if (ch >= '0' && ch <= '9')
                    num = (int)(ch - '0');
                else
                    throw new FormatException();
                int len = text.Length;
                while (index < len)
                {
                    ch = text[index++];
                    if (ch >= '0' && ch <= '9')
                        num = num * 10 + (int)(ch - '0');
                    else
                        throw new FormatException();
                }
                if (neg)
                    num = -num;
                return num;
            }
        }

        /// <summary>
        /// Parses the int64. This expect exactly correct input format or it will throw exception.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <param name="startIndex">The index in text.</param>
        /// <param name="subLen">The length of substring in text.</param>
        /// <param name="hex">if set to <c>true</c> then text is hexadecimal but without the prefix!</param>
        /// <returns></returns>
        /// <exception cref="System.FormatException">
        /// </exception>
        public static long ParseInt64ExactFast(string text, int startIndex, int subLen, bool hex)
        {
            if (hex)
            {
                long num;
                char ch = text[startIndex];
                if (ch >= '0' && ch <= '9')
                    num = (int)(ch - '0');
                else if (ch >= 'a' && ch <= 'f')
                    num = (int)ch - 87;
                else if (ch >= 'A' && ch <= 'F')
                    num = (int)ch - 55;
                else
                    throw new FormatException();

                int index = startIndex + 1;
                int len = subLen + startIndex;
                while (index < len)
                {
                    ch = text[index++];
                    if (ch >= '0' && ch <= '9')
                        num = num * 16 + (int)(ch - '0');
                    else if (ch >= 'a' && ch <= 'f')
                        num = num * 16 + ((int)ch - 87);
                    else if (ch >= 'A' && ch <= 'F')
                        num = num * 16 + ((int)ch - 55);
                    else
                        throw new FormatException();
                }
                return num;
            }
            else
            {
                long num;
                bool neg = false;
                char ch = text[startIndex];
                int index = startIndex + 1;
                if (ch == '-')
                {
                    neg = true;
                    index = startIndex + 2;

                    ch = text[startIndex + 1];
                    if (ch >= '0' && ch <= '9')
                        num = (int)(ch - '0');
                    else
                        throw new FormatException();
                }
                else if (ch >= '0' && ch <= '9')
                    num = (int)(ch - '0');
                else
                    throw new FormatException();
                int len = startIndex + subLen;
                while (index < len)
                {
                    ch = text[index++];
                    if (ch >= '0' && ch <= '9')
                        num = num * 10 + (int)(ch - '0');
                    else
                        throw new FormatException();
                }
                if (neg)
                    num = -num;
                return num;
            }
        }

        /// <summary>
        /// Tries to parse int64.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <param name="result">The result.</param>
        /// <returns></returns>
        public static bool TryParseInt64(string text, out long result)
        {
            result = 0;

            text = (text ?? "").Trim();
            if (text.Length == 0)
                return false;

            bool isHex = false;
            bool isNeg = false;

            if (text.Length >= 1)
            {
                if (text[0] == '-')
                {
                    isNeg = true;
                    text = text.Substring(1);
                }
                else if (text[0] == '+')
                {
                    text = text.Substring(1);
                }
            }

            if (text.Length >= 2 && text[0] == '0' && (text[1] == 'x' || text[1] == 'X'))
            {
                isHex = true;
                text = text.Substring(2);
            }

            if (!isHex && text.Length >= 1 && text[text.Length - 1] == 'h')
            {
                isHex = true;
                text = text.Substring(0, text.Length - 1);
            }

            if (text.Length == 0)
                return false;

            long v = 0;
            if (isHex)
            {
                if (!text.All(q => CheckNumber_HexaDecimal(q)))
                    return false;

                if (!long.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out v))
                    return false;
            }
            else
            {
                if (!text.All(q => CheckNumber_Decimal(q)))
                    return false;

                if (!long.TryParse(text, out v))
                    return false;
            }

            if (isNeg)
                v = -v;

            result = v;
            return true;
        }

        /// <summary>
        /// Tries to parse uint64.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <param name="result">The result.</param>
        /// <returns></returns>
        public static bool TryParseUInt64(string text, out ulong result)
        {
            result = 0;

            text = (text ?? "").Trim();
            if (text.Length == 0)
                return false;

            bool isHex = false;

            if (text.Length >= 2 && text[0] == '0' && (text[1] == 'x' || text[1] == 'X'))
            {
                isHex = true;
                text = text.Substring(2);
            }

            if (!isHex && text.Length >= 1 && text[text.Length - 1] == 'h')
            {
                isHex = true;
                text = text.Substring(0, text.Length - 1);
            }

            if (text.Length == 0)
                return false;

            ulong v = 0;
            if (isHex)
            {
                if (!text.All(q => CheckNumber_HexaDecimal(q)))
                    return false;

                if (!ulong.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out v))
                    return false;
            }
            else
            {
                if (!text.All(q => CheckNumber_Decimal(q)))
                    return false;

                if (!ulong.TryParse(text, out v))
                    return false;
            }

            result = v;
            return true;
        }
        
        private static bool CheckNumber_Decimal(char ch)
        {
            return ch >= '0' && ch <= '9';
        }

        private static bool CheckNumber_HexaDecimal(char ch)
        {
            return (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F');
        }
        
        /// <summary>
        /// Finds the keyword.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <param name="keyword">The keyword.</param>
        /// <param name="caseSensitive">Is the keyword case sensitive?</param>
        /// <returns></returns>
        public static int FindKeyword(string text, string keyword, bool caseSensitive = true)
        {
            if (text != null)
            {
                int start = 0;
                StringComparison comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                while (true)
                {
                    int ix = text.IndexOf(keyword, start, comparison);
                    if (ix < 0)
                        break;

                    if (IsDelimiter(text, ix - 1) && IsDelimiter(text, ix + keyword.Length))
                        return ix;

                    start = ix + 1;
                }
            }

            return -1;
        }

        /// <summary>
        /// Determines whether the specified text index is delimiter.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <param name="index">The index.</param>
        /// <returns></returns>
        private static bool IsDelimiter(string text, int index)
        {
            if (index < 0 || index >= text.Length)
                return true;

            char ch = text[index];
            if (char.IsLetterOrDigit(ch) || ch == '_')
                return false;

            return true;
        }

        /// <summary>
        /// Compares the lists.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="first">The first.</param>
        /// <param name="second">The second.</param>
        /// <returns></returns>
        public static bool CompareList<T>(IReadOnlyList<T> first, IReadOnlyList<T> second)
        {
            if (first == null)
                return second == null;
            if (second == null)
                return false;
            if (first.Count != second.Count)
                return false;

            for (int i = 0; i < first.Count; i++)
            {
                if (!first[i].Equals(second[i]))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Combines the hash codes.
        /// </summary>
        /// <param name="first">The first hash code.</param>
        /// <param name="second">The second hash code.</param>
        /// <returns></returns>
        public static int CombineHashCode(int first, int second)
        {
            return ((first << 5) + first) ^ second;
        }
        
        /// <summary>
        /// Check if two nullable offsets are equal.
        /// </summary>
        /// <param name="first">The first.</param>
        /// <param name="second">The second.</param>
        /// <returns></returns>
        public static bool EqualsNullableOffset(Offset? first, Offset? second)
        {
            if (first.HasValue != second.HasValue)
                return false;
            if (first.HasValue)
                return first.Value == second.Value;
            return true;
        }

        /// <summary>
        /// Check if two nullable offsets are equal.
        /// </summary>
        /// <param name="first">The first.</param>
        /// <param name="second">The second.</param>
        /// <returns></returns>
        public static bool EqualsNullableOffset(Offset? first, int? second)
        {
            if (first.HasValue != second.HasValue)
                return false;
            if (first.HasValue)
                return first.Value == second.Value;
            return true;
        }
        
        /// <summary>
        /// Determines whether there is overlap between two ranges.
        /// </summary>
        /// <param name="firstMin">The first minimum.</param>
        /// <param name="firstMax">The first maximum.</param>
        /// <param name="secondMin">The second minimum.</param>
        /// <param name="secondMax">The second maximum.</param>
        /// <param name="allowCollide">if set to <c>true</c> then allow edges to collide without it counting as overlap (returns false if firstMax == secondMin).</param>
        /// <returns></returns>
        public static bool IsOverlap(long firstMin, long firstMax, long secondMin, long secondMax, bool allowCollide = true)
        {
            if (allowCollide)
            {
                if (secondMin >= firstMax)
                    return false;
                if (secondMax <= firstMin)
                    return false;
            }
            else
            {
                if (secondMin > firstMax)
                    return false;
                if (secondMax < firstMin)
                    return false;
            }

            return true;
        }

        public static void TestStringDistanceSpeed()
        {
            var map = new DamerauLevensteinMetric();
            int times = 1000;
            int different = 100;
            double avgRatio = 0.9;
            var rnd = new Random(123);

            int[] size = new int[] { 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000 };
            var sw = new System.Diagnostics.Stopwatch();
            for(int i = 0; i < size.Length; i++)
            {
                Console.WriteLine("Testing speed for size " + size[i] + " x " + times + " ...");
                var lsa = new List<ulong[]>();
                var lsb = new List<ulong[]>();

                for(int j = 0; j < different; j++)
                {
                    var a = new ulong[size[i]];
                    var b = new ulong[size[i]];
                    lsa.Add(a);
                    lsb.Add(b);

                    if (j < 10)
                    {
                        for(int k = 0; k < size[i]; k++)
                        {
                            a[k] = 1000;
                            b[k] = 1000;
                        }
                        continue;
                    }

                    double ratio = avgRatio;
                    if (j > 80)
                        ratio = 0.2;

                    for(int k = 0; k < size[i]; k++)
                    {
                        if(rnd.NextDouble() < ratio)
                        {
                            a[k] = 1000;
                            b[k] = 1000;
                        }
                        else
                        {
                            a[k] = (uint)rnd.Next(0, 100) + 1000;
                            b[k] = (uint)rnd.Next(0, 100) + 1000;
                        }
                    }
                }

                sw.Restart();
                for(int j = 0; j < times; j++)
                {
                    int ri = j % different;
                    int diff = map.GetDistance(lsa[ri], lsb[ri], int.MaxValue);
                }
                sw.Stop();

                long ms = sw.ElapsedTicks * 1000 / System.Diagnostics.Stopwatch.Frequency;
                Console.WriteLine(" ... " + ms + " ms.");
            }
        }
        
        /// <summary>
        /// Swaps the specified parameters.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="first">The first.</param>
        /// <param name="second">The second.</param>
        public static void Swap<T>(ref T first, ref T second)
        {
            var tmp = first;
            first = second;
            second = tmp;
        }

        public static class DistCachePerm
        {
            public static void ResetCache(byte type)
            {
                var map = new Dictionary<ulong, int>();
                foreach(var pair in Map)
                {
                    byte t = (byte)((pair.Key >> 8) & 0xFF);
                    if (t == type)
                        continue;

                    map[pair.Key] = pair.Value;
                }
                Map = map;
            }

            internal static bool Can(IDADiffCalculator.Migration.IDAObjectComparisonData.IDAObjectComparisonTypes type)
            {
                switch(type)
                {
                    case Migration.IDAObjectComparisonData.IDAObjectComparisonTypes.InRef:
                    case Migration.IDAObjectComparisonData.IDAObjectComparisonTypes.OutRef:
                        return IDADiffCalculator.Migration.IDAMigrate.SeparateRefPass;
                }

                return true;
            }

            public static bool Should(IReadOnlyList<ulong> a, IReadOnlyList<ulong> b)
            {
                if (a.Count >= 0x80 && b.Count >= 0x80)
                    return true;

                return false;
            }

            public static ulong MakeKey(int firstIndex, int secondIndex, byte type, byte seg, bool firstIsOther, bool secondIsOther)
            {
#if DEBUG
                if ((firstIndex & 0x007FFFFF) != firstIndex)
                    throw new ArgumentException();
                if ((secondIndex & 0x007FFFFF) != secondIndex)
                    throw new ArgumentException();
#endif

                if (firstIsOther)
                    firstIndex |= 0x800000;
                if (secondIsOther)
                    secondIndex |= 0x800000;

                ulong ux = unchecked((uint)firstIndex);
                ux <<= 24;

                uint uy = unchecked((uint)secondIndex);
                ux |= uy;
                ux <<= 8;
                ux |= type;
                ux <<= 8;
                ux |= seg;
                return ux;
            }

            public static int Lookup(ulong key)
            {
                int x;
                if (!Map.TryGetValue(key, out x))
                    return -1;
                return x;
            }

            public static void Set(ulong key, int value)
            {
                Map[key] = value;
            }

            private static Dictionary<ulong, int> Map = new Dictionary<ulong, int>();
        }

        public sealed class DistCache
        {
            public DistCache(int index, int width, int offset)
            {
                int sz = (width + 7) / 8;
                this.Values = new byte[sz];
                this.Mask = new byte[sz];
                this.Index = index;
                this.Offset = offset;
                this.Size = sz * 8;
                this.Half = this.Size / 2;
            }

            private readonly int Index;
            private readonly int Offset;
            private readonly int Size;
            private readonly int Half;
            private readonly byte[] Values;
            private readonly byte[] Mask;

            private int CalculateIndex(int index)
            {
                int i = (this.Index - this.Offset) - index + this.Half;
                if (i >= this.Size)
                    return -1;
                return i;
            }

            public int Get(int index)
            {
                int ri = this.CalculateIndex(index);
                if (ri < 0)
                    return 0;

                int i = ri / 8;
                int j = ri % 8;
                byte fl = (byte)(1 << j);
                if ((this.Mask[i] & fl) == 0)
                    return 0;

                return (this.Values[i] & fl) != 0 ? 1 : -1;
            }

            public void Set(int index, bool value)
            {
                int ri = this.CalculateIndex(index);
                if (ri < 0)
                    return;

                int i = ri / 8;
                int j = ri % 8;
                byte fl = (byte)(1 << j);
                this.Mask[i] |= fl;
                if (value)
                    this.Values[i] |= fl;
            }

            public void Clear()
            {
                for (int i = 0; i < this.Mask.Length; i++)
                {
                    this.Mask[i] = 0;
                    this.Values[i] = 0;
                }
            }
        }

        public sealed class DistCache2
        {
            public DistCache2(int index, int width)
            {
                this.Index = index;
                this.Values = new byte[width / 8];
                this.Mask = new byte[width / 8];
            }

            private readonly byte[] Values;
            private readonly byte[] Mask;
            private readonly int Index;
            private int Offset;
            
            public void SetOffset(int offset)
            {
                offset /= 8;

                if (this.Offset == offset)
                    return;

                int diff = offset - this.Offset;
                if (diff > 0)
                {
                    for(int i = 0; i < this.Values.Length; i++)
                    {
                        int j = i + diff;
                        if (j < this.Values.Length)
                        {
                            this.Values[i] = this.Values[j];
                            this.Mask[i] = this.Mask[j];
                        }
                        else
                        {
                            this.Values[i] = 0;
                            this.Mask[i] = 0;
                        }
                    }
                }
                else
                {
                    for(int i = this.Values.Length - 1; i >= 0; i--)
                    {
                        int j = i + diff;
                        if (j >= 0)
                        {
                            this.Values[i] = this.Values[j];
                            this.Mask[i] = this.Mask[j];
                        }
                        else
                        {
                            this.Values[i] = 0;
                            this.Mask[i] = 0;
                        }
                    }
                }

                this.Offset = offset;
            }

            private int GetRealIndex(int index)
            {
                return (this.Values.Length * 4) + (index - (this.Index + this.Offset * 8));
            }

            public int GetCached(int index)
            {
                int realIndex = this.GetRealIndex(index);
                if (realIndex < 0 || realIndex >= this.Values.Length * 8)
                {
#if DEBUG
                    //throw new ArgumentException();
                    return -1;
#else
                    return -1;
#endif
                }

                int i = realIndex / 8;
                int j = realIndex % 8;
                byte fl = (byte)(1 << j);

                byte m = this.Mask[i];
                if ((m & fl) == 0)
                    return -1;

                byte b = this.Values[i];
                return (b & fl) != 0 ? 1 : 0;
            }

            public void SetCached(int index, bool value)
            {
                int realIndex = this.GetRealIndex(index);
                if (realIndex < 0 || realIndex >= this.Values.Length * 8)
                    return;

                int i = realIndex / 8;
                int j = realIndex % 8;
                byte fl = (byte)(1 << j);

                this.Mask[i] |= fl;
                if (value)
                    this.Values[i] |= fl;
            }

            public static void Test()
            {
                for (int i = 0; i < 2; i++)
                {
                    var a = new DistCache2(1000, 100);

                    int v;
                    int checkIndex = 1010;

                    if (i == 1)
                        checkIndex -= 30;

                    if ((v = a.GetCached(checkIndex)) != -1)
                        throw new ArgumentException("v = " + v.ToString());

                    a.SetCached(checkIndex, true);

                    if ((v = a.GetCached(checkIndex)) != 1)
                        throw new ArgumentException("v = " + v.ToString());

                    a.SetOffset(15);

                    if ((v = a.GetCached(checkIndex)) != 1)
                        throw new ArgumentException("v = " + v.ToString());

                    if (a.Values.Count(q => q != 0) != 1 || a.Values.Count(q => q == 0) != a.Values.Length - 1)
                        throw new ArgumentException("a.Values.Count");

                    a.SetOffset(-10);

                    if ((v = a.GetCached(checkIndex)) != 1)
                        throw new ArgumentException("v = " + v.ToString());

                    if (a.Values.Count(q => q != 0) != 1 || a.Values.Count(q => q == 0) != a.Values.Length - 1)
                        throw new ArgumentException("a.Values.Count");
                }
            }
        }

        public sealed class DamerauLevensteinMetric
        {
            private int[] _buf0 = new int[1024];
            private int[] _buf1 = new int[1024];
            private int[] _buf2 = new int[1024];

            /// <summary>
            /// Computes the Damerau-Levenshtein Distance between two strings, represented as arrays of
            /// integers, where each integer represents the code point of a character in the source string.
            /// Includes an optional threshhold which can be used to indicate the maximum allowable distance.
            /// </summary>
            /// <param name="source">An array of the code points of the first string</param>
            /// <param name="target">An array of the code points of the second string</param>
            /// <param name="threshold">Maximum allowable distance</param>
            /// <returns>Int.MaxValue if threshhold exceeded; otherwise the Damerau-Leveshteim distance between the strings</returns>
            public int GetDistance(ulong[] source, ulong[] target, int threshold)
            {
                int length1 = source.Length;
                int length2 = target.Length;

                // Return trivial case - difference in string lengths exceeds threshhold
                if (Math.Abs(length1 - length2) > threshold)
                    return threshold + 1;

                // Few special cases to speed up stuff
                if(length1 == length2)
                {
                    if(length1 == 1)
                        return source[0] == target[0] ? 0 : 1;
                    if(length1 == 2)
                    {
                        if (source[0] == target[0]) // equal or 1 change
                            return source[1] == target[1] ? 0 : 1;
                        if (source[1] == target[1]) // one change
                            return 1;
                        if (source[0] == target[1] && source[1] == target[0]) // 1 transposition
                            return 1;
                        return 2; // need full replace
                    }
                }

                // Ensure arrays [i] / length1 use shorter length 
                if (length1 > length2)
                {
                    Swap(ref target, ref source);
                    Swap(ref length1, ref length2);
                }

                int maxi = length1;
                int maxj = length2;

                if(_buf0.Length < maxi + 1)
                {
                    _buf0 = new int[maxi + 1];
                    _buf1 = new int[maxi + 1];
                    _buf2 = new int[maxi + 1];
                }

                int[] dCurrent = _buf0;
                int[] dMinus1 = _buf1;
                int[] dMinus2 = _buf2;

                int[] dSwap;

                for (int i = 0; i <= maxi; i++)
                    dCurrent[i] = i;

                int jm1 = 0, im1 = 0, im2 = -1;

                for (int j = 1; j <= maxj; j++)
                {
                    // Rotate
                    dSwap = dMinus2;
                    dMinus2 = dMinus1;
                    dMinus1 = dCurrent;
                    dCurrent = dSwap;

                    // Initialize
                    int minDistance = int.MaxValue;
                    dCurrent[0] = j;
                    im1 = 0;
                    im2 = -1;

                    for (int i = 1; i <= maxi; i++)
                    {
                        var sim1 = source[im1];
                        var tjm1 = target[jm1];
                        int cost = sim1 == tjm1 ? 0 : 1;

                        int del = dCurrent[im1] + 1;
                        int ins = dMinus1[i] + 1;
                        int sub = dMinus1[im1] + cost;

                        //Fastest execution for min value of 3 integers
                        int min = (del > ins) ? (ins > sub ? sub : ins) : (del > sub ? sub : del);

                        if (i > 1 && j > 1 && source[im2] == tjm1 && sim1 == target[j - 2])
                            min = Math.Min(min, dMinus2[im2] + cost);

                        dCurrent[i] = min;
                        if (min < minDistance)
                            minDistance = min;
                        im1++;
                        im2++;
                    }
                    jm1++;
                    if (minDistance > threshold)
                        return threshold + 1;
                }

                int result = dCurrent[maxi];
                return (result > threshold) ? (threshold + 1) : result;
            }
        }

        /// <summary>
        /// Damerau-Levenshtein distance calculator.
        /// </summary>
        public sealed class DamerauLevensteinMetric_wiki
        {
            // https://en.wikibooks.org/wiki/Algorithm_Implementation/Strings/Levenshtein_distance#C#

            private int[] _currentRow = new int[256];
            private int[] _previousRow = new int[256];
            private int[] _transpositionRow = new int[256];
            
            /// <summary>
            /// Damerau-Levenshtein distance is computed in asymptotic time O((max + 1) * min(first.length(), second.length()))
            /// </summary>
            /// <param name="first">The first array.</param>
            /// <param name="second">The second array.</param>
            /// <param name="max">Maximum distance to look at.</param>
            /// <returns></returns>
            public int GetDistance(IReadOnlyList<ulong> first, IReadOnlyList<ulong> second, int max)
            {
                int firstLength = first.Count;
                int secondLength = second.Count;

                if (firstLength == 0)
                    return secondLength;

                if (secondLength == 0)
                    return firstLength;

                if (firstLength > secondLength)
                {
                    var tmp = first;
                    first = second;
                    second = tmp;
                    firstLength = secondLength;
                    secondLength = second.Count;
                }
                else if(firstLength == secondLength)
                {
                    if (firstLength == 1)
                        return first[0] == second[0] ? 0 : 1;
                    else if(firstLength == 2)
                    {
                        var a0 = first[0];
                        var a1 = first[1];
                        var b0 = second[0];
                        var b1 = second[1];

                        if (a0 == b0)
                            return a1 == b1 ? 0 : 1;
                        if (a1 == b1)
                            return 1;
                        return a0 == b1 && a1 == b0 ? 1 : 2;
                    }
                }

                if (max < 0 || max > secondLength)
                    max = secondLength;
                if (secondLength - firstLength > max)
                    return max + 1;

                if (firstLength > _currentRow.Length)
                {
                    _currentRow = new int[firstLength + 1];
                    _previousRow = new int[firstLength + 1];
                    _transpositionRow = new int[firstLength + 1];
                }

                for (int i = 0; i <= firstLength; i++)
                    _previousRow[i] = i;

                ulong lastSecondCh = 0;
                for (int i = 1; i <= secondLength; i++)
                {
                    var secondCh = second[i - 1];
                    _currentRow[0] = i;

                    // Compute only diagonal stripe of width 2 * (max + 1).
                    int from = Math.Max(i - max - 1, 1);
                    int to = Math.Min(i + max + 1, firstLength);

                    ulong lastFirstCh = 0;
                    for (int j = from; j <= to; j++)
                    {
                        var firstCh = first[j - 1];

                        // Compute minimal cost of state change to current state from previous states of deletion, insertion and swapping.
                        int cost = firstCh == secondCh ? 0 : 1;
                        int value = Math.Min(Math.Min(_currentRow[j - 1] + 1, _previousRow[j] + 1), _previousRow[j - 1] + cost);

                        // If there was transposition, take into account its cost.
                        if (firstCh == lastSecondCh && secondCh == lastFirstCh && j > 1)
                            value = Math.Min(value, _transpositionRow[j - 2] + cost);

                        _currentRow[j] = value;
                        lastFirstCh = firstCh;
                    }
                    lastSecondCh = secondCh;

                    int[] tempRow = _transpositionRow;
                    _transpositionRow = _previousRow;
                    _previousRow = _currentRow;
                    _currentRow = tempRow;
                }

                return _previousRow[firstLength];
            }
        }
    }
}
