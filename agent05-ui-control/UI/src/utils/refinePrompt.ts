/** Which prompt a refine run goes with (agent05 `POST /api/jobs/{id}/refine`, field `prompt`). */
export type RefinePrompt = 'lesson' | 'saved';

/** What the operator picked, kept together with the job it was picked for. */
export interface RefinePromptPick {
  jobId: string;
  prompt: RefinePrompt;
}

/**
 * The prompt a run of this job starts with: what the operator picked for this very job, else the
 * lesson prompt for a job that holds a lesson (started from the lessons list, or a dialogue) and
 * the saved settings for a plain upload. A pick made for another job does not carry over.
 */
export function refinePromptFor(
  jobId: string | null | undefined,
  lesson: boolean | null | undefined,
  pick: RefinePromptPick | null
): RefinePrompt {
  if (pick && jobId && pick.jobId === jobId) return pick.prompt;
  return lesson ? 'lesson' : 'saved';
}
