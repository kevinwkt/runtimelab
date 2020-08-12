﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Xunit;

namespace System.Text.Json.SourceGeneration.UnitTests
{
    public class TypeWrapperTests
    {
        [Fact]
        public void MetadataLoadFilePathHandle()
        {
            // Create a MetadataReference from new code.
            string referencedSource = @"
              namespace ReferencedAssembly
              {
                public class ReferencedType {
                    public int ReferencedPublicInt;     
                    public double ReferencedPublicDouble;     
                }
            }";

            // Compile the referenced assembly first.
            Compilation referencedCompilation = CompilationHelper.CreateCompilation(referencedSource);

            // Emit the image of the referenced assembly.
            byte[] referencedImage;
            using (MemoryStream ms = new MemoryStream())
            {
                var emitResult = referencedCompilation.Emit(ms);
                if (!emitResult.Success)
                {
                    throw new InvalidOperationException();
                }
                referencedImage = ms.ToArray();
            }

            string source = @"
            using System.Text.Json.Serialization;
            using ReferencedAssembly;

              namespace HelloWorld
              {
                [JsonSerializable]
                public class MyType {
                    public void MyMethod() { }
                    public void MySecondMethod() { }
                }
                [JsonSerializable(typeof(ReferencedType))]
                public static partial class ExternType { }
              }";

            MetadataReference[] additionalReferences = { MetadataReference.CreateFromImage(referencedImage) };

            // Compilation using the referenced image should fail if out MetadataLoadContext does not handle.
            Compilation compilation = CompilationHelper.CreateCompilation(source, additionalReferences);

            JsonSerializerSourceGenerator generator = new JsonSerializerSourceGenerator();

            Compilation newCompilation = CompilationHelper.RunGenerators(compilation, out ImmutableArray<Diagnostic> generatorDiags, generator);

            // Make sure compilation was successful.
            Assert.Empty(generatorDiags);
            Assert.Empty(newCompilation.GetDiagnostics());

            // Should find both types since compilation above was successful.
            Assert.Equal(2, generator.FoundTypes.Count);
        }

        [Fact]
        public void CanGetAttributes()
        {
            string source = @"
            using System;
            using System.Text.Json.Serialization;

              namespace HelloWorld
              {
                [JsonSerializable]
                public class MyType {

                    [JsonInclude]
                    public double PublicDouble;
                    [JsonPropertyName(""PPublicDouble"")]
                    public char PublicChar;
                    [JsonIgnore]
                    private double PrivateDouble;
                    private char PrivateChar;

                    public MyType() {{ }}
                    [JsonConstructor]
                    public MyType(double d) {{ PrivateDouble = d; }}

                    [JsonPropertyName(""TestName"")]
                    public int PublicPropertyInt { get; set; }
                    [JsonExtensionData]
                    public string PublicPropertyString { get; set; }
                    [JsonIgnore]
                    private int PrivatePropertyInt { get; set; }
                    private string PrivatePropertyString { get; set; }

                    [Obsolete(""Testing"", true)]
                    public void MyMethod() { }
                    public void MySecondMethod() { }
                }
              }";

            Compilation compilation = CompilationHelper.CreateCompilation(source);

            JsonSerializerSourceGenerator generator = new JsonSerializerSourceGenerator();

            Compilation outCompilation = CompilationHelper.RunGenerators(compilation, out ImmutableArray<Diagnostic> generatorDiags, generator);

            // Check base functionality of found types.
            Assert.Equal(1, generator.FoundTypes.Count);
            Type foundType = generator.FoundTypes.First().Value;

            Assert.Equal("HelloWorld.MyType", foundType.FullName);

            // Check for ConstructorInfoWrapper attribute usage.
            (string, string[])[] receivedCtorsWithAttributeNames = foundType.GetConstructors().Select(ctor => (ctor.DeclaringType.FullName, ctor.GetCustomAttributesData().Cast<CustomAttributeData>().Select(attributeData => attributeData.AttributeType.Name).ToArray())).ToArray();
            Assert.Equal(
                receivedCtorsWithAttributeNames,
                new (string, string[])[] {
                    ("HelloWorld.MyType", new string[] { }),
                    ("HelloWorld.MyType", new string[] { "JsonConstructorAttribute" })
                });

            // Check for MethodInfoWrapper attribute usage.
            (string, string[])[] receivedMethodsWithAttributeNames = foundType.GetMethods().Select(method => (method.Name, method.GetCustomAttributesData().Cast<CustomAttributeData>().Select(attributeData => attributeData.AttributeType.Name).ToArray())).Where(x => x.Item2.Any()).ToArray();
            Assert.Equal(
                receivedMethodsWithAttributeNames,
                new (string, string[])[] { ("MyMethod", new string[] { "ObsoleteAttribute" }) });

            // Check for FieldInfoWrapper attribute usage.
            (string, string[])[] receivedFieldsWithAttributeNames = foundType.GetFields().Select(field => (field.Name, field.GetCustomAttributesData().Cast<CustomAttributeData>().Select(attributeData => attributeData.AttributeType.Name).ToArray())).Where(x => x.Item2.Any()).ToArray();
            Assert.Equal(
                receivedFieldsWithAttributeNames,
                new (string, string[])[] {
                    ("PublicDouble", new string[] { "JsonIncludeAttribute" }),
                    ("PublicChar", new string[] { "JsonPropertyNameAttribute" }),
                    ("PrivateDouble", new string[] { "JsonIgnoreAttribute" } )
                });

            // Check for PropertyInfoWrapper attribute usage.
            (string, string[])[] receivedPropertyWithAttributeNames  = foundType.GetProperties().Select(property => (property.Name, property.GetCustomAttributesData().Cast<CustomAttributeData>().Select(attributeData => attributeData.AttributeType.Name).ToArray())).Where(x => x.Item2.Any()).ToArray();
            Assert.Equal(
                receivedPropertyWithAttributeNames,
                new (string, string[])[] {
                    ("PublicPropertyInt", new string[] { "JsonPropertyNameAttribute" }),
                    ("PublicPropertyString", new string[] { "JsonExtensionDataAttribute" }),
                    ("PrivatePropertyInt", new string[] { "JsonIgnoreAttribute" } )
                });

            // Check for MemberInfoWrapper attribute usage.
            (string, string[])[] receivedMembersWithAttributeNames = foundType.GetMembers().Select(member => (member.Name, member.GetCustomAttributesData().Cast<CustomAttributeData>().Select(attributeData => attributeData.AttributeType.Name).ToArray())).Where(x => x.Item2.Any()).ToArray();
            Assert.Equal(
                receivedMembersWithAttributeNames,
                new (string, string[])[] {
                    ("PublicDouble", new string[] { "JsonIncludeAttribute" }),
                    ("PublicChar", new string[] { "JsonPropertyNameAttribute" }),
                    ("PrivateDouble", new string[] { "JsonIgnoreAttribute" } ),
                    (".ctor", new string[] { "JsonConstructorAttribute" }),
                    ("PublicPropertyInt", new string[] { "JsonPropertyNameAttribute" }),
                    ("PublicPropertyString", new string[] { "JsonExtensionDataAttribute" }),
                    ("PrivatePropertyInt", new string[] { "JsonIgnoreAttribute" } ),
                    ("MyMethod", new string[] { "ObsoleteAttribute" }),
                });
        }
    }
}
