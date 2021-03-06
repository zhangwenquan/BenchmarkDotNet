﻿using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Parameters;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace BenchmarkDotNet.Order
{
    public class DefaultOrderProvider : IOrderProvider
    {
        public static readonly IOrderProvider Instance = new DefaultOrderProvider();

        private readonly IComparer<ParameterInstances> paramsComparer = ParameterComparer.Instance;
        private readonly IComparer<Job> jobComparer = JobComparer.Instance;
        private readonly IComparer<Target> targetComparer;

        private readonly SummaryOrderPolicy summaryOrderPolicy;

        public DefaultOrderProvider(
            SummaryOrderPolicy summaryOrderPolicy = SummaryOrderPolicy.Default, 
            MethodOrderPolicy methodOrderPolicy = MethodOrderPolicy.Declared)
        {
            this.summaryOrderPolicy = summaryOrderPolicy;
            targetComparer = new TargetComparer(methodOrderPolicy);
        }

        public virtual IEnumerable<Benchmark> GetExecutionOrder(Benchmark[] benchmarks)
        {
            var list = benchmarks.ToList();
            list.Sort(new BenchmarkComparer(paramsComparer, jobComparer, targetComparer));
            return list;
        }

        public virtual IEnumerable<Benchmark> GetSummaryOrder(Benchmark[] benchmarks, Summary summary)
        {
            var benchmarkLogicalKeys = benchmarks.Select(b => GetLogicalGroupKey(summary.Config, benchmarks, b)).ToArray();
            foreach (string logicalGroupKey in GetLogicalGroupOrder(benchmarkLogicalKeys.Distinct()))
            {
                var groupBenchmarks = benchmarks.Where((b, index) => benchmarkLogicalKeys[index] == logicalGroupKey).ToArray();
                foreach (var benchmark in GetSummaryOrderForGroup(groupBenchmarks, summary))
                    yield return benchmark;
            }            
        }
        
        protected virtual IEnumerable<Benchmark> GetSummaryOrderForGroup(Benchmark[] benchmarks, Summary summary)
        {            
            switch (summaryOrderPolicy)
            {
                case SummaryOrderPolicy.FastestToSlowest:
                    return benchmarks.OrderBy(b => summary[b].ResultStatistics.Mean);
                case SummaryOrderPolicy.SlowestToFastest:
                    return benchmarks.OrderByDescending(b => summary[b].ResultStatistics.Mean);
                default:
                    return GetExecutionOrder(benchmarks);
            }
        }

        public string GetHighlightGroupKey(Benchmark benchmark) =>
            summaryOrderPolicy == SummaryOrderPolicy.Default
            ? benchmark.Parameters.DisplayInfo
            : null;

        public string GetLogicalGroupKey(IConfig config, Benchmark[] allBenchmarks, Benchmark benchmark)
        {
            var rules = new HashSet<BenchmarkLogicalGroupRule>(config.GetLogicalGroupRules());
            if (allBenchmarks.Any(b => b.Job.Meta.IsBaseline))
            {
                rules.Add(BenchmarkLogicalGroupRule.ByMethod);
                rules.Add(BenchmarkLogicalGroupRule.ByParams);
            }
            if (allBenchmarks.Any(b => b.Target.Baseline))
            {
                rules.Add(BenchmarkLogicalGroupRule.ByJob);
                rules.Add(BenchmarkLogicalGroupRule.ByParams);
            }

            var keys = new List<string>();            
            if (rules.Contains(BenchmarkLogicalGroupRule.ByMethod))
                keys.Add(benchmark.Target.DisplayInfo);
            if (rules.Contains(BenchmarkLogicalGroupRule.ByJob))
                keys.Add(benchmark.Job.DisplayInfo);
            if (rules.Contains(BenchmarkLogicalGroupRule.ByParams))
                keys.Add(benchmark.Parameters.DisplayInfo);
            if (rules.Contains(BenchmarkLogicalGroupRule.ByCategory))
                keys.Add(string.Join(",", benchmark.Target.Categories));

            string logicalGroupKey = string.Join("-", keys.Where(key => key != string.Empty));
            return logicalGroupKey == string.Empty ? "*" : logicalGroupKey;
        }

        public IEnumerable<string> GetLogicalGroupOrder(IEnumerable<string> logicalGroups) => logicalGroups.OrderBy(s => s);

        public bool SeparateLogicalGroups => true;

        private class BenchmarkComparer : IComparer<Benchmark>
        {
            private readonly IComparer<ParameterInstances> paramsComparer;
            private readonly IComparer<Job> jobComparer;
            private readonly IComparer<Target> targetComparer;

            public BenchmarkComparer(IComparer<ParameterInstances> paramsComparer, IComparer<Job> jobComparer, IComparer<Target> targetComparer)
            {
                this.targetComparer = targetComparer;
                this.jobComparer = jobComparer;
                this.paramsComparer = paramsComparer;
            }

            public int Compare(Benchmark x, Benchmark y) => new[]
            {
                paramsComparer?.Compare(x.Parameters, y.Parameters) ?? 0,
                jobComparer?.Compare(x.Job, y.Job) ?? 0,
                targetComparer?.Compare(x.Target, y.Target) ?? 0,
                string.CompareOrdinal(x.DisplayInfo, y.DisplayInfo)
            }.FirstOrDefault(c => c != 0);
        }
    }
}