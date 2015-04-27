﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using NuGet.ContentModel;
using NuGet.Frameworks;
using NuGet.RuntimeModel;

namespace NuGet.Client
{
    /// <summary>
    /// Defines all the package conventions used by Managed Code packages
    /// </summary>
    public class ManagedCodeConventions
    {
        private static readonly ContentPropertyDefinition TfmProperty = new ContentPropertyDefinition(PropertyNames.TargetFrameworkMoniker,
            table: new Dictionary<string, object>() {
                { "any", new NuGetFramework(FrameworkConstants.SpecialIdentifiers.Any, FrameworkConstants.EmptyVersion) }
            },
            parser: TargetFrameworkName_Parser,
            compatibilityTest: TargetFrameworkName_CompatibilityTest);

        private static readonly ContentPropertyDefinition AnyProperty = new ContentPropertyDefinition(PropertyNames.AnyValue);
        private static readonly ContentPropertyDefinition AssemblyProperty = new ContentPropertyDefinition(PropertyNames.ManagedAssembly, fileExtensions: new[] { ".dll" });

        private RuntimeGraph _runtimeGraph;

        public CriteriaSet Criteria { get; }
        public IReadOnlyDictionary<string, ContentPropertyDefinition> Properties { get; }
        public PatternSet Patterns { get; }

        public ManagedCodeConventions(RuntimeGraph runtimeGraph)
        {
            _runtimeGraph = runtimeGraph;

            var props = new Dictionary<string, ContentPropertyDefinition>();
            props[TfmProperty.Name] = TfmProperty;
            props[AnyProperty.Name] = AnyProperty;
            props[AssemblyProperty.Name] = AssemblyProperty;

            props[PropertyNames.RuntimeIdentifier] = new ContentPropertyDefinition(
                PropertyNames.RuntimeIdentifier,
                compatibilityTest: RuntimeIdentifier_CompatibilityTest);

            Properties = new ReadOnlyDictionary<string, ContentPropertyDefinition>(props);

            Criteria = new CriteriaSet(this);
            Patterns = new PatternSet(this);
        }

        private bool RuntimeIdentifier_CompatibilityTest(object criteria, object available)
        {
            if (_runtimeGraph == null)
            {
                return Equals(criteria, available);
            }
            else
            {
                string criteriaRid = criteria as string;
                string availableRid = available as string;

                if (criteriaRid != null && availableRid != null)
                {
                    return _runtimeGraph.AreCompatible(criteriaRid, availableRid);
                }
                return false;
            }
        }

        private static object TargetFrameworkName_Parser(string name)
        {
            if (name.Contains('.') || name.Contains('/'))
            {
                return null;
            }

            if (name == "contract")
            {
                return null;
            }

            var result = NuGetFramework.Parse(name);

            if (!result.IsUnsupported)
            {
                return result;
            }

            return new NuGetFramework(name, new Version(0, 0));
        }

        private static bool TargetFrameworkName_CompatibilityTest(object criteria, object available)
        {
            var criteriaFrameworkName = criteria as NuGetFramework;
            var availableFrameworkName = available as NuGetFramework;

            if (criteriaFrameworkName != null && availableFrameworkName != null)
            {
                // We only consider 'any' matches when the criteria explicitly asks for them
                if (criteriaFrameworkName.IsAny && availableFrameworkName.IsAny)
                {
                    return true;
                }
                else if (criteriaFrameworkName.IsAny || availableFrameworkName.IsAny)
                {
                    // Otherwise, ignore 'any' framework values
                    return false;
                }
                return DefaultCompatibilityProvider.Instance.IsCompatible(criteriaFrameworkName, availableFrameworkName);
            }

            return false;
        }

        private static Version NormalizeVersion(Version version)
        {
            return new Version(version.Major,
                               version.Minor,
                               Math.Max(version.Build, 0),
                               Math.Max(version.Revision, 0));
        }

        public class CriteriaSet
        {
            private ManagedCodeConventions _conventions;

            internal CriteriaSet(ManagedCodeConventions conventions)
            {
                _conventions = conventions;
            }

            public SelectionCriteria ForFrameworkAndRuntime(NuGetFramework framework, string runtimeIdentifier)
            {
                var builder = new SelectionCriteriaBuilder(_conventions.Properties);
                if (!string.IsNullOrEmpty(runtimeIdentifier))
                {
                    builder = builder
                        // Take runtime-specific matches first!
                        .Add[PropertyNames.TargetFrameworkMoniker, framework][PropertyNames.RuntimeIdentifier, runtimeIdentifier]
                        .Add[PropertyNames.TargetFrameworkMoniker, FrameworkConstants.CommonFrameworks.Core50][PropertyNames.RuntimeIdentifier, runtimeIdentifier]
                        .Add[PropertyNames.TargetFrameworkMoniker, FrameworkConstants.SpecialIdentifiers.Any][PropertyNames.RuntimeIdentifier, runtimeIdentifier];
                }

                // Then try runtime-agnostic
                builder = builder
                    .Add[PropertyNames.TargetFrameworkMoniker, framework]
                    .Add[PropertyNames.TargetFrameworkMoniker, FrameworkConstants.CommonFrameworks.Core50]
                    .Add[PropertyNames.TargetFrameworkMoniker, FrameworkConstants.SpecialIdentifiers.Any];

                return builder.Criteria;
            }

            public SelectionCriteria ForFramework(NuGetFramework framework)
            {
                return ForFrameworkAndRuntime(framework, runtimeIdentifier: null);
            }

            public SelectionCriteria ForRuntime(string runtimeIdentifier)
            {
                var builder = new SelectionCriteriaBuilder(_conventions.Properties);
                builder = builder
                    .Add[PropertyNames.RuntimeIdentifier, runtimeIdentifier];
                return builder.Criteria;
            }
        }

        public class PatternSet
        {
            public ContentPatternDefinition RuntimeAssemblies { get; }
            public ContentPatternDefinition CompileAssemblies { get; }
            public ContentPatternDefinition NativeLibraries { get; }

            internal PatternSet(ManagedCodeConventions conventions)
            {
                RuntimeAssemblies = new ContentPatternDefinition(
                    conventions.Properties,
                    groupPatterns: new[]
                    {
                        "runtimes/{rid}/lib/{tfm}/{any?}",
                        "lib/{tfm}/{any?}",
                    },
                    pathPatterns: new[]
                    {
                        "runtimes/{rid}/lib/{tfm}/{assembly}",
                        "lib/{tfm}/{assembly}",
                    });

                CompileAssemblies = new ContentPatternDefinition(
                    conventions.Properties,
                    groupPatterns: new[]
                    {
                        "ref/{tfm}/{any?}",
                    },
                    pathPatterns: new[] {
                        "ref/{tfm}/{assembly}",
                    });

                NativeLibraries = new ContentPatternDefinition(
                    conventions.Properties,
                    groupPatterns: new[]
                    {
                        "runtimes/{rid}/native/{any?}",
                        "native/{any?}",
                    },
                    pathPatterns: new[]
                    {
                        "runtimes/{rid}/native/{any}",
                        "native/{any}",
                    });
            }
        }

        public static class PropertyNames
        {
            public static readonly string TargetFrameworkMoniker = "tfm";
            public static readonly string RuntimeIdentifier = "rid";
            public static readonly string AnyValue = "any";
            public static readonly string ManagedAssembly = "assembly";

        }
    }
}
