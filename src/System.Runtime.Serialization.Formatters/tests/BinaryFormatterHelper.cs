﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text.RegularExpressions;
using Xunit;

namespace System.Runtime.Serialization.Formatters.Tests
{
    public partial class BinaryFormatterTests
    {
        private static void CheckForAnyEquals(object obj, object deserializedObj)
        {
            Assert.True(CheckEquals(obj, deserializedObj), "Error during equality check of type " + obj?.GetType()?.FullName);
        }

        public static bool CheckEquals(object objA, object objB)
        {
            if (objA == null && objB == null)
                return true;

            if (objA != null && objB != null)
            {
                object equalityResult = null;
                Type objType = objA.GetType();

                // Check if custom equality extension method is available
                MethodInfo customEqualityCheck = GetExtensionMethod(objType);
                if (customEqualityCheck != null)
                {
                    equalityResult = customEqualityCheck.Invoke(objA, new object[] { objA, objB });
                }
                else
                {
                    // Check if object.Equals(object) is overridden and if not check if there is a more concrete equality check implementation
                    bool equalsNotOverridden = objType.GetMethod("Equals", new Type[] { typeof(object) }).DeclaringType == typeof(object);
                    if (equalsNotOverridden)
                    {
                        // If type doesn't override Equals(object) method then check if there is a more concrete implementation
                        // e.g. if type implements IEquatable<T>.
                        MethodInfo equalsMethod = objType.GetMethod("Equals", new Type[] { objType });
                        if (equalsMethod.DeclaringType != typeof(object))
                        {
                            equalityResult = equalsMethod.Invoke(objA, new object[] { objB });
                        }
                    }
                }

                if (equalityResult != null)
                {
                    return (bool)equalityResult;
                }
            }

            if (objA is IEnumerable objAEnumerable && objB is IEnumerable objBEnumerable)
            {
                return CheckSequenceEquals(objAEnumerable, objBEnumerable);
            }

            return objA.Equals(objB);
        }

        public static bool CheckSequenceEquals(IEnumerable a, IEnumerable b)
        {
            if (a == null || b == null)
                return a == b;

            if (a.GetType() != b.GetType())
                return false;

            IEnumerator eA = null;
            IEnumerator eB = null;

            try
            {
                eA = (a as IEnumerable).GetEnumerator();
                eB = (a as IEnumerable).GetEnumerator();
                while (true)
                {
                    bool moved = eA.MoveNext();
                    if (moved != eB.MoveNext())
                        return false;
                    if (!moved)
                        return true;
                    if (eA.Current == null && eB.Current == null)
                        return true;
                    if (!CheckEquals(eA.Current, eB.Current))
                        return true;
                }
            }
            finally
            {
                (eA as IDisposable)?.Dispose();
                (eB as IDisposable)?.Dispose();
            }
        }

        private static MethodInfo GetExtensionMethod(Type extendedType)
        {
            if (extendedType.IsGenericType)
            {
                MethodInfo method = typeof(EqualityExtensions).GetMethods()
                        ?.SingleOrDefault(m =>
                            m.Name == "IsEqual" &&
                            m.GetParameters().Length == 2 &&
                            m.GetParameters()[0].ParameterType.Name == extendedType.Name &&
                            m.IsGenericMethodDefinition);
                if (method != null)
                    return method.MakeGenericMethod(extendedType.GenericTypeArguments[0]);
            }

            return typeof(EqualityExtensions).GetMethod("IsEqual", new[] { extendedType, extendedType });
        }

        public static string GetTestDataFilePath()
        {
            string GetRepoRootPath()
            {
                var exeFile = new FileInfo(Assembly.GetExecutingAssembly().Location);

                DirectoryInfo root = exeFile.Directory;
                while (!Directory.Exists(Path.Combine(root.FullName, ".git")))
                {
                    if (root.Parent == null)
                        return null;

                    root = root.Parent;
                }

                return root.FullName;
            }

            // Get path to binary formatter test data
            string repositoryRootPath = GetRepoRootPath();
            Assert.NotNull(repositoryRootPath);
            string testDataFilePath = Path.Combine(repositoryRootPath, "src", "System.Runtime.Serialization.Formatters", "tests", "BinaryFormatterTestData.cs");
            Assert.True(File.Exists(testDataFilePath));

            return testDataFilePath;
        }

        public static IEnumerable<object[]> GetCoreTypeRecords()
        {
            // Define all core type records here
            return SerializableEqualityComparers_MemberData()
                .Concat(SerializableObjects_MemberData());
        }

        public static IEnumerable<string> GetCoreTypeBlobs(IEnumerable<object[]> records)
        {
            foreach (object[] record in records)
            {
                BinaryFormatter bf = new BinaryFormatter();
                using (MemoryStream ms = new MemoryStream())
                {
                    bf.Serialize(ms, record[0]); // Zero Index is the core type instance
                    yield return Convert.ToBase64String(ms.ToArray());
                }
            }
        }

        public static void UpdateCoreTypeBlobs(string testDataFilePath, string[] blobs)
        {
            // Replace existing test data blobs with updated ones
            string[] testDataLines = File.ReadAllLines(testDataFilePath);
            List<string> updatedTestDataLines = new List<string>();
            int numberOfBlobs = 0;

            for (int i = 0; i < testDataLines.Length; i++)
            {
                string testDataLine = testDataLines[i];
                if (!testDataLine.Trim().StartsWith("yield") || numberOfBlobs >= blobs.Length)
                {
                    updatedTestDataLines.Add(testDataLine);
                    continue;
                }

                if (PlatformDetection.IsFullFramework)
                {
                    testDataLine = Regex.Replace(testDataLine, ", \"AAEAAAD[^\"]+\"(?!,)", ", \"" + blobs[numberOfBlobs] + "\"");
                }
                else
                {
                    testDataLine = Regex.Replace(testDataLine, "\"AAEAAAD[^\"]+\",", "\"" + blobs[numberOfBlobs] + "\",");
                }

                updatedTestDataLines.Add(testDataLine);
                numberOfBlobs++;
            }

            // Check if all blobs were recognized and write updates to file
            Assert.Equal(numberOfBlobs, blobs.Length);
            File.WriteAllLines(testDataFilePath, updatedTestDataLines);
        }

        public static byte[] SerializeObjectToRaw(object obj)
        {
            BinaryFormatter bf = new BinaryFormatter();
            using (MemoryStream ms = new MemoryStream())
            {
                bf.Serialize(ms, obj);
                return ms.ToArray();
            }
        }

        public static string SerializeObjectToBlob(object obj)
        {
            byte[] raw = SerializeObjectToRaw(obj);
            return Convert.ToBase64String(raw);
        }

        public static object DeserializeRawToObject(byte[] raw)
        {
            var binaryFormatter = new BinaryFormatter();
            using (var serializedStream = new MemoryStream(raw))
            {
                return binaryFormatter.Deserialize(serializedStream);
            }
        }

        public static object DeserializeBlobToObject(string base64Str)
        {
            byte[] raw = Convert.FromBase64String(base64Str);
            return DeserializeRawToObject(raw);
        }

        private static T FormatterClone<T>(
            T obj,
            ISerializationSurrogate surrogate = null,
            FormatterAssemblyStyle assemblyFormat = FormatterAssemblyStyle.Full,
            TypeFilterLevel filterLevel = TypeFilterLevel.Full,
            FormatterTypeStyle typeFormat = FormatterTypeStyle.TypesAlways)
        {
            BinaryFormatter f;
            if (surrogate == null)
            {
                f = new BinaryFormatter();
            }
            else
            {
                var c = new StreamingContext();
                var s = new SurrogateSelector();
                s.AddSurrogate(obj.GetType(), c, surrogate);
                f = new BinaryFormatter(s, c);
            }
            f.AssemblyFormat = assemblyFormat;
            f.FilterLevel = filterLevel;
            f.TypeFormat = typeFormat;

            using (var s = new MemoryStream())
            {
                f.Serialize(s, obj);
                Assert.NotEqual(0, s.Position);
                s.Position = 0;
                return (T)(f.Deserialize(s));
            }
        }

        private class DelegateBinder : SerializationBinder
        {
            public Func<string, string, Type> BindToTypeDelegate = null;
            public override Type BindToType(string assemblyName, string typeName) => BindToTypeDelegate?.Invoke(assemblyName, typeName);
        }
    }
}
