using System.Text;
using Enoch.Protocol;
using Enoch.Storage.FileSystem;
using Xunit;

namespace Enoch.Backend.Tests;
public sealed class FileSystemRunStoreTests
{
 [Fact] public async Task LifecycleAndRecoveryAreDurable()
 { var root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString()); var s=new FileSystemRunStore(root); var run=await s.StartAsync(new("demo",new{prompt="hello"},"run1")); await s.PublishPlanAsync("run1",new(new{steps=new[]{"work"}})); await s.PublishEventAsync("run1",new("e1","progress",new{percent=50})); await s.AddEvidenceAsync("run1",new("notes","done")); await using var a=new MemoryStream(Encoding.UTF8.GetBytes("payload")); await s.AddArtifactAsync("run1","out.txt","text/plain",a); await s.PublishResultAsync("run1",new(new{answer="ok"})); var finished=await s.FinishAsync("run1",new(RunOutcome.Success,"complete")); Assert.Equal(RunState.Finished,finished.Manifest.State); var recovered=new FileSystemRunStore(root); var loaded=await recovered.GetAsync("run1"); Assert.Single(loaded.Artifacts); Assert.Single(loaded.Events); Assert.NotNull(loaded.Result); Assert.Equal("complete",loaded.Manifest.Summary); Assert.Contains("ok",loaded.Result!.ToString()); }
 [Fact] public async Task DuplicateEventIsIdempotentAndDoesNotAdvanceSequence()
 { var s=new FileSystemRunStore(Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString())); await s.StartAsync(new("x",new{},"run1")); var a=await s.PublishEventAsync("run1",new("same","progress")); var b=await s.PublishEventAsync("run1",new("same","progress",new{different=true})); Assert.Equal(a.Sequence,b.Sequence); Assert.Equal(a.EventId,b.EventId); Assert.Equal(a.Sequence,(await s.GetAsync("run1")).Manifest.Sequence); }
 [Fact] public async Task FinishedRunRejectsMutation()
 { var s=new FileSystemRunStore(Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString())); await s.StartAsync(new("x",new{},"run1")); await s.FinishAsync("run1",new(RunOutcome.Failed)); var ex=await Assert.ThrowsAsync<EnochException>(()=>s.PublishEventAsync("run1",new("e","progress"))); Assert.Equal("run_finished",ex.Code); }
 [Fact] public async Task ExpectedSequenceRejectsStaleWriter()
 { var s=new FileSystemRunStore(Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString())); await s.StartAsync(new("x",new{},"run1")); await s.PublishEventAsync("run1",new("e","progress",ExpectedSequence:0)); var ex=await Assert.ThrowsAsync<EnochException>(()=>s.PublishEventAsync("run1",new("e2","progress",ExpectedSequence:0))); Assert.Equal("sequence_conflict",ex.Code); }
 [Fact] public async Task WaitResumeAndChecksumsArePersisted()
 { var root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString()); var s=new FileSystemRunStore(root); await s.StartAsync(new("x",new{},"run1")); Assert.Equal(RunState.Waiting,(await s.WaitAsync("run1")).Manifest.State); Assert.Equal(RunState.Running,(await s.ResumeAsync("run1")).Manifest.State); var checksum=Path.Combine(root,"runs","run1","checksums.sha256"); Assert.True(File.Exists(checksum)); Assert.Contains("manifest.json",await File.ReadAllTextAsync(checksum)); }
}
