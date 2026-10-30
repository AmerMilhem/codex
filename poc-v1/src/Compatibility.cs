using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Quartz;

public class ProbeJob:IJob {
 public static TaskCompletionSource Fired=new(TaskCreationOptions.RunContinuationsAsynchronously);
 public ValueTask Execute(IJobExecutionContext context,CancellationToken cancellationToken){Fired.TrySetResult();return ValueTask.CompletedTask;}
}
class ProbeExporter:BaseExporter<Activity>{public int Count;public override ExportResult Export(in Batch<Activity> batch){foreach(var item in batch)Interlocked.Increment(ref Count);return ExportResult.Success;}}
partial class P {
 static async Task CompatibilityExtras(){
  await Run("quartz4-net10-in-memory-trigger","compatibility",async()=>{var scheduler=await QuartzSchedulerBuilder.Create().BuildScheduler();await scheduler.Start();await scheduler.ScheduleJob(JobBuilder.Create<ProbeJob>().WithIdentity("poc-only").Build(),TriggerBuilder.Create().StartNow().Build());await ProbeJob.Fired.Task.WaitAsync(TimeSpan.FromSeconds(10));await scheduler.Shutdown(true);return new{quartz=typeof(IScheduler).Assembly.GetName().Version?.ToString(),executed=1,limitation="RAM store only; persistent PostgreSQL job store clustering/schema upgrades pending"};});
  await Run("opentelemetry-net10-span-export","compatibility",()=>{var exporter=new ProbeExporter();using var provider=Sdk.CreateTracerProviderBuilder().AddSource("erp-poc-v1").AddProcessor(new SimpleActivityExportProcessor(exporter)).Build();using(var source=new ActivitySource("erp-poc-v1")){using var span=source.StartActivity("posting-poc");Check(span!=null,"span absent");span!.SetTag("tenant.id",A.Tenant.ToString());span.SetTag("result","synthetic");}provider.ForceFlush();Check(exporter.Count==1,"no exported span");return Task.FromResult<object?>(new{spans=exporter.Count,version=typeof(Sdk).Assembly.GetName().Version?.ToString(),limitation="local exporter, hosted OTLP collector and redaction policy verification pending"});});
 }
}
