import { describe, expect, it } from 'vitest';
import { refinePromptFor } from './refinePrompt';

describe('refinePromptFor', () => {
  it('offers the lesson prompt first for a job that holds a lesson', () => {
    expect(refinePromptFor('job-a', true, null)).toBe('lesson');
  });

  it('offers the saved settings first for a plain upload', () => {
    expect(refinePromptFor('job-a', false, null)).toBe('saved');
    expect(refinePromptFor('job-a', undefined, null)).toBe('saved');
  });

  it('keeps what the operator picked for this job', () => {
    const pick = { jobId: 'job-a', prompt: 'saved' as const };
    expect(refinePromptFor('job-a', true, pick)).toBe('saved');
    expect(
      refinePromptFor('job-a', false, { jobId: 'job-a', prompt: 'lesson' })
    ).toBe('lesson');
  });

  it('does not carry a pick over to another job', () => {
    const pick = { jobId: 'job-a', prompt: 'saved' as const };
    expect(refinePromptFor('job-b', true, pick)).toBe('lesson');
  });

  it('has nothing picked while no job is open', () => {
    const pick = { jobId: 'job-a', prompt: 'lesson' as const };
    expect(refinePromptFor(null, false, pick)).toBe('saved');
  });
});
