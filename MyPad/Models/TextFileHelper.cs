﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Ude;

namespace MyPad.Models
{
    public static class TextFileHelper
    {
        private const int DETECT_ENCODING_SAMPLING_SIZE = 10240;

        /// <summary>
        /// 指定されたバイト配列の文字コードを推定します。検証は配列の先頭から既定のバイト数分の要素を取り出して行われます。
        /// </summary>
        /// <param name="bytes">バイト配列</param>
        /// <returns>文字コード</returns>
        public static Encoding DetectEncodingFast(byte[] bytes)
            => DetectEncoding(bytes, DETECT_ENCODING_SAMPLING_SIZE);

        /// <summary>
        /// 指定されたバイト配列の文字コードを推定します。検証は配列内の全要素を走査して行われます。
        /// </summary>
        /// <param name="bytes">バイト配列</param>
        /// <returns>文字コード</returns>
        public static Encoding DetectEncoding(byte[] bytes)
            => DetectEncoding(bytes, 0);

        /// <summary>
        /// 指定されたバイト配列の文字コードを推定します。
        /// </summary>
        /// <param name="bytes">バイト配列</param>
        /// <param name="verifiedLength">検証される先頭からのバイト長</param>
        /// <returns>文字コード</returns>
        public static Encoding DetectEncoding(byte[] bytes, int verifiedLength)
        {
            const int MIN_LENGTH = 256;

            // 空の場合は null
            if (bytes.Length == 0)
                return null;

            // BOM による判定
            var bom = DetectEncodingByBom(bytes);
            if (bom != null)
                return bom;

            // バイト配列の整形
            if (bytes.Length < MIN_LENGTH)
            {
                // バイト長が短すぎる場合は複製して補う
                var count = MIN_LENGTH / bytes.Length + 1;
                var buffer = new byte[count * bytes.Length];
                for (var i = 0; i < count; i++)
                    Array.Copy(bytes, 0, buffer, bytes.Length * i, bytes.Length);
                bytes = buffer;
            }
            else if (MIN_LENGTH <= verifiedLength && verifiedLength < bytes.Length)
            {
                // バイト長が指定されている場合は切り出す
                var buffer = new byte[verifiedLength];
                Array.Copy(bytes, 0, buffer, 0, verifiedLength);
                bytes = buffer;
            }

            // Mozilla Universal Charset Detector による判定
            var ude = new CharsetDetector();
            ude.Feed(bytes, 0, bytes.Length);
            ude.DataEnd();
            if (string.IsNullOrEmpty(ude.Charset) == false)
                return Encoding.GetEncoding(ude.Charset);

            // いずれによっても判定できない場合は null
            return null;
        }

        /// <summary>
        /// バイト準記号に基づき文字コードを推定します。
        /// </summary>
        /// <param name="bytes">バイト配列</param>
        /// <returns>文字コード</returns>
        private static Encoding DetectEncodingByBom(byte[] bytes)
        {
            if (bytes.Length <= 1)
                return null;

            if (3 <= bytes.Length)
                if (bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf)
                    return new UTF8Encoding(true);             // UTF8

            Encoding result = null;
            if (2 <= bytes.Length)
            {
                if (bytes[0] == 0xfe && bytes[1] == 0xff)
                    result = new UnicodeEncoding(true, true);  // UTF16-BE
                if (bytes[0] == 0xff && bytes[1] == 0xfe)
                    result = new UnicodeEncoding(false, true); // UTF16-LE (UTF32-LE の可能性もある)
            }
            if (4 <= bytes.Length)
            {
                if (bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xfe && bytes[3] == 0xff)
                    result = new UTF32Encoding(true, true);    // UTF32-BE
                if (bytes[0] == 0xff && bytes[1] == 0xfe && bytes[2] == 0x00 && bytes[3] == 0x00)
                    result = new UTF32Encoding(false, true);   // UTF32-LE
            }
            return result;
        }

        /// <summary>
        /// 指定されたディレクトリ以下に存在するファイルに対して文字列の検索を行います。
        /// </summary>
        /// <param name="output">検索結果を格納するコレクション</param>
        /// <param name="targetText">検索する文字列</param>
        /// <param name="rootPath">検索対象のディレクトリ</param>
        /// <param name="defaultEncoding">既定の文字コード (<paramref name="autoDetectEncoding"/> が false の場合は常にこの文字コードが使用されます。)</param>
        /// <param name="searchPattern">ファイルの種類</param>
        /// <param name="allDirectories">検索対象にサブディレクトリも含めるかどうかを示す値</param>
        /// <param name="ignoreCase">大文字と小文字を区別するかどうかを示す値</param>
        /// <param name="useRegex">正規表現を使用するかどうかを示す値</param>
        /// <param name="autoDetectEncoding">文字コードを自動判別するかどうかを示す値</param>
        /// <param name="bufferSize">並列実行されるチャンクのサイズ</param>
        /// <returns>タスクの情報</returns>
        public static async Task Grep(Collection<object> output, string targetText, string rootPath, Encoding defaultEncoding, string searchPattern = "*", bool allDirectories = true, bool ignoreCase = true, bool useRegex = false, bool autoDetectEncoding = true, int bufferSize = 30)
        {
            if (defaultEncoding == null)
                throw new ArgumentNullException(nameof(defaultEncoding));

            if (string.IsNullOrEmpty(searchPattern))
                searchPattern = "*";

            foreach (var chunk in Directory.EnumerateFiles(rootPath, searchPattern, allDirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly).Buffer(bufferSize))
            {
                var chunkResult = new ConcurrentBag<object>();
                await Task.WhenAll(chunk.Select(path =>
                    Task.Run(() =>
                    {
                        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            // 文字コードを推定する
                            var encoding = defaultEncoding;
                            if (autoDetectEncoding)
                            {
                                var buffer = new byte[DETECT_ENCODING_SAMPLING_SIZE];
                                stream.Read(buffer, 0, buffer.Length);
                                stream.Position = 0;
                                encoding = DetectEncoding(buffer) ?? defaultEncoding;
                            }

                            // 比較用のメソッドを定義する
                            Func<string, bool> func;
                            if (useRegex)
                                func = (text) => Regex.IsMatch(text, targetText);
                            else if (ignoreCase)
                                func = (text) => 0 <= text.IndexOf(targetText, StringComparison.OrdinalIgnoreCase);
                            else
                                func = (text) => 0 <= text.IndexOf(targetText, StringComparison.Ordinal);

                            // テキストを検索する
                            using (var reader = new StreamReader(stream, encoding))
                            {
                                for (var i = 1; 0 <= reader.Peek(); i++)
                                {
                                    var text = reader.ReadLine();
                                    if (func(text))
                                        chunkResult.Add(new { Path = path, Line = i, Text = text, Encoding = encoding });
                                }
                            }
                        }
                    }))
                    .ToList());
                output.AddRange(chunkResult);
                GC.Collect();
            }
        }
    }
}