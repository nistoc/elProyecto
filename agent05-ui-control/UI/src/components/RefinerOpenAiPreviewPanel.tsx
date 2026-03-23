export interface RefinerOpenAiPreviewPanelProps {
  openAiRequestPreview?: string | null;
  t: (key: string) => string;
}

/**
 * OpenAI request preview (last prompt body), shown under result-section on the refiner step.
 */
export function RefinerOpenAiPreviewPanel({
  openAiRequestPreview,
  t,
}: RefinerOpenAiPreviewPanelProps) {
  const hasPreview =
    openAiRequestPreview != null && openAiRequestPreview.length > 0;
  if (!hasPreview) return null;

  return (
    <>
      <div className="refiner-stream-threads__openai">
        <div className="refiner-stream-threads__openai-head">
          {t('refinerOpenAiPreviewTitle')}
        </div>
        <p className="refiner-stream-threads__openai-desc">
          {t('refinerOpenAiPreviewHint')}
        </p>
        <pre className="refiner-stream-threads__openai-pre">{openAiRequestPreview}</pre>
      </div>
      <style>{`
        .refiner-stream-threads__openai {
          margin-bottom: 1rem;
          padding-bottom: 0.75rem;
          border-bottom: 1px solid var(--color-border);
        }
        .refiner-stream-threads__openai-head {
          font-size: 0.85rem;
          font-weight: 600;
          color: var(--color-heading);
          margin-bottom: 0.35rem;
        }
        .refiner-stream-threads__openai-desc {
          margin: 0 0 0.5rem 0;
          font-size: 0.75rem;
          color: var(--color-text-secondary);
        }
        .refiner-stream-threads__openai-pre {
          max-height: 260px;
          margin: 0;
          overflow: auto;
          padding: 0.5rem 0.6rem;
          border: 1px solid var(--color-border-strong);
          border-radius: 6px;
          background: var(--color-surface);
          font-family: ui-monospace, 'Cascadia Code', 'Consolas', monospace;
          font-size: 0.72rem;
          line-height: 1.45;
          white-space: pre-wrap;
          word-break: break-word;
        }
      `}</style>
    </>
  );
}
