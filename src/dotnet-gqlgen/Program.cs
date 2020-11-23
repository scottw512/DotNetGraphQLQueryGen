﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using Newtonsoft.Json;
using RazorLight;

namespace dotnet_gqlgen
{
    public class Program
    {
        [Argument(0, Description = "Path to the GraphQL schema file or a GraphQL introspection endpoint")]
        [Required]
        public string Source { get; }

        [Option(LongName = "header", ShortName = "h", Description = "Headers to pass to GraphQL introspection endpoint. Use \"Authorization=Bearer eyJraWQ,X-API-Key=abc,...\"")]
        public string HeaderValues { get; }

        [Option(LongName = "namespace", ShortName = "n", Description = "Namespace to generate code under")]
        public string Namespace { get; } = "Generated";

        [Option(LongName = "client_class_name", ShortName = "c", Description = "Name for the client class")]
        public string ClientClassName { get; } = "GraphQLClient";
        [Option(LongName = "scalar_mapping", ShortName = "m", Description = "Map of custom schema scalar types to dotnet types. Use \"GqlType=DotNetClassName,ID=Guid,...\"")]
        public string ScalarMapping { get; }
        [Option(LongName = "output", ShortName = "o", Description = "Output directory")]
        public string OutputDir { get; } = "output";

        public Dictionary<string, string> dotnetToGqlTypeMappings = new Dictionary<string, string> {
            {"string", "String"},
            {"String", "String"},
            {"int", "Int!"},
            {"Int32", "Int!"},
            {"double", "Float!"},
            {"bool", "Boolean!"},
        };

        public static Task<int> Main(string[] args) => CommandLineApplication.ExecuteAsync<Program>(args);

        private async void OnExecute()
        {
            try
            {
                Uri uriResult;
                bool isGraphQlEndpoint = Uri.TryCreate(Source, UriKind.Absolute, out uriResult)
                                && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);

                string schemaText = null;
                bool isIntroSpectionFile = false;

                if (isGraphQlEndpoint)
                {
                    Console.WriteLine($"Loading from {Source}...");
                    using (var httpClient = new HttpClient())
                    {
                        foreach (var header in SplitMultiValueArgument(HeaderValues))
                        {
                            httpClient.DefaultRequestHeaders.Add(header.Key, header.Value);
                        }

                        Dictionary<string, string> request = new Dictionary<string, string>();
                        request["query"] = IntroSpectionQuery.Query;
                        request["operationName"] = "IntrospectionQuery";

                        var response = httpClient
                            .PostAsync(Source, 
                            new StringContent(JsonConvert.SerializeObject(request), Encoding.UTF8, "application/json")).GetAwaiter().GetResult();

                        schemaText = await response.Content.ReadAsStringAsync();
                        isIntroSpectionFile = true;
                    }
                }
                else
                {
                    Console.WriteLine($"Loading {Source}...");
                    schemaText = File.ReadAllText(Source);
                    isIntroSpectionFile = Path.GetExtension(Source).Equals(".json", StringComparison.OrdinalIgnoreCase);
                }                

                var mappings = new Dictionary<string, string>();
                if (!string.IsNullOrEmpty(ScalarMapping))
                {
                    SplitMultiValueArgument(ScalarMapping).ToList().ForEach(i => {
                        dotnetToGqlTypeMappings[i.Value] = i.Key;
                        mappings[i.Key] = i.Value;
                    });
                }

                // parse into AST
                var typeInfo = !isIntroSpectionFile ?
                    SchemaCompiler.Compile(schemaText, mappings) :
                    IntrospectionCompiler.Compile(schemaText, mappings);

                Console.WriteLine($"Generating types in namespace {Namespace}, outputting to {ClientClassName}.cs");

                // pass the schema to the template
                var engine = new RazorLightEngineBuilder()
                    .UseEmbeddedResourcesProject(typeof(Program))
                    .UseMemoryCachingProvider()
                    .Build();

                var allTypes = typeInfo.Types.Concat(typeInfo.Inputs).ToDictionary(k => k.Key, v => v.Value);

                var resultBuilder = new StringBuilder();
                foreach (var typ in allTypes)
                {
                    resultBuilder.Append($"public class {typ.Key}{Environment.NewLine}");
                    resultBuilder.Append($"{{{Environment.NewLine}");
                    foreach (var fld in typ.Value.Fields)
                    {
                        if (fld.Name.Contains("birth"))
                        {
                            var sdf = 55;
                        }
                        var fieldType = fld.DotNetType;
                        if (fld.IsScalar && !fld.IsNonNullable && ! fld.IsArray && !fieldType.EndsWith("?"))
                        {
                            fieldType = $"{fieldType}?";
                        }
                        resultBuilder.Append($"\tpublic {fieldType} {fld.DotNetName} {{ get; set; }}{Environment.NewLine}");
                    }
                    resultBuilder.Append($"}}{Environment.NewLine}");

                    var graphClassName = typ.Value.IsInput ? "InputObjectGraphType" : "ObjectGraphType";
                    resultBuilder.Append($"public class {typ.Key}Impl : {graphClassName}<{typ.Key}>{Environment.NewLine}");
                    resultBuilder.Append($"{{{Environment.NewLine}");
                    resultBuilder.Append($"\tpublic {typ.Key}Impl(){Environment.NewLine}");
                    resultBuilder.Append($"\t{{{Environment.NewLine}");

                    resultBuilder.Append($"\t\tName = nameof({typ.Key});{Environment.NewLine}{Environment.NewLine}");
                    foreach (var fld in typ.Value.Fields)
                    {
                        var fldType = Type.GetType(fld.DotNetType);

                        if (fldType != null)
                        {
                            var gg = 66;
                        }
                        var nullable = fld.IsNonNullable ? "false" : "true";

                        if (typ.Value.IsInput)
                        {
                            //Field<IntGraphType>("id");
                            //resultBuilder.Append($"\t\tField<{GetInputGraphType(fld.DotNetType)}GraphType>(x => x.{fld.DotNetName}).Name(\"{fld.Name}\");{Environment.NewLine}");
                            //resultBuilder.Append($"\t\tField<{GetInputGraphType(fld.DotNetType)}GraphType>(\"{fld.Name}\");{Environment.NewLine}");
                            //Field("MyField", x => x.Id, nullable: true, type: typeof(IntGraphType));
                            //var nullableStr = typ.Value.
                            resultBuilder.Append($"\t\tField(\"{fld.Name}\", x => x.{fld.DotNetName}, nullable: {nullable}, type: typeof({GetInputGraphType(fld)}GraphType));{Environment.NewLine}");

                        }
                        else
                        {
                            if (fld.IsScalar)
                            {
                                resultBuilder.Append($"\t\tField(\"{fld.Name}\", x => x.{fld.DotNetName}, nullable: {nullable});{Environment.NewLine}");
                            }
                            else
                            {
                                resultBuilder.Append($"\t\tField(\"{fld.Name}\", x => x.{fld.DotNetName}, nullable: {nullable}, type: typeof({GetInputGraphType(fld)}));{Environment.NewLine}");
                            }
                            //resultBuilder.Append($"\t\tField(x => x.{fld.DotNetName}).Name(\"{fld.Name}\");{Environment.NewLine}");

                            //Field("date_of_birth", x => x.Date_of_birth, nullable: true);
                        }
                    }

                    resultBuilder.Append($"\t}}{Environment.NewLine}");
                    resultBuilder.Append($"}}{Environment.NewLine}");
                }
                //string result = await engine.CompileRenderAsync("schemaImport.cshtml", new
                //{
                //    Namespace = Namespace,
                //    SchemaFile = Source,
                //    Types = allTypes,
                //    Enums = typeInfo.Enums,
                //    Mutation = typeInfo.Mutation,
                //    CmdArgs = $"-n {Namespace} -c {ClientClassName} -m {ScalarMapping}"
                //});
                Directory.CreateDirectory(OutputDir);
                File.WriteAllText($"{OutputDir}/GeneratedTypes.cs", resultBuilder.ToString());

                //result = await engine.CompileRenderAsync("client.cshtml", new
                //{
                //    Namespace = Namespace,
                //    SchemaFile = Source,
                //    Query = typeInfo.Query,
                //    Mutation = typeInfo.Mutation,
                //    ClientClassName = ClientClassName,
                //    Mappings = dotnetToGqlTypeMappings
                //});
                //File.WriteAllText($"{OutputDir}/{ClientClassName}.cs", result);

                Console.WriteLine($"Done.");
            }
            catch (Exception e)
            {
                Console.WriteLine("Error: " + e.ToString());
            }
        }

        /// <summary>
        /// Splits an argument value like "value1=v1,value2=v2" into a dictionary.
        /// </summary>
        /// <remarks>Very simple splitter. Eg can't handle comma's or equal signs in values</remarks>
        private Dictionary<string, string> SplitMultiValueArgument(string arg)
        {
            if (string.IsNullOrEmpty(arg))
            {
                return new Dictionary<string, string>();
            }

            return arg
                .Split(',')
                .Select(h => h.Split('='))
                .Where(hs => hs.Length >= 2)
                .ToDictionary(key => key[0], value => value[1]);
        }

        private string GetInputGraphType(Field typ)
        {
            var typClean = typ.DotNetType.Replace("?", "");
            return $"{typClean.Substring(0, 1).ToUpper()}{typClean.Substring(1)}";
        }
    }
}
