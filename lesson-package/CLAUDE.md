# Lesson package — instructions for a chat that works on this tool

Read this file before changing anything of the lesson package: its screen in XtractManager, its API in
agent05, its files on disk, or the prompts that build its lists. The other files of this folder:

| File | What it holds |
|---|---|
| [CONTRACT.md](CONTRACT.md) | The files (`package.json`, `marks.json`), the ids of the items, which fields may be edited, the API |
| [BUILDER.md](BUILDER.md) | How the lists are built: the passes, their models and price, what was measured, what is weak |
| [prompts/](prompts/) | The prompt of every pass, one file each |

## What the tool is

The owner learns Spanish with a teacher; the lessons are recorded and transcribed by this repository. A
**lesson package** is what one lesson gives the learner, made from the transcripts of its recordings:

- **topics** — what was talked about, in order, with the times;
- **grammar** — the rules that were *spoken of* in the lesson (explained, asked about, a mistake corrected),
  merged into themes, each with its examples from the recording;
- **examples** — Spanish phrases given as a pattern, with a translation and what they show;
- **new words** — words that were explained, asked about, corrected or prompted;
- **vocabulary** — other words of the lesson worth keeping;
- **words found by code** — Latin-letter words of the lesson that no list holds.

The screen is a view of XtractManager (agent05 UI), opened from a lesson by the button «Пакет урока».

## What the owner asked for

These are requirements, not ideas. A change that breaks one of them needs the owner's word first.

1. **The purpose** (2026-11-16): from the dialogues of a lesson — vocabulary, the topics mentioned, examples,
   the grammar discussed, new words and terms, and finding the new words said in the lesson.
2. **Only what is really new, by default** (2026-11-18): the word lists of a lesson are long, yet against the
   earlier lessons only some tens of words are new. So every word is counted against **all earlier
   lessons**, the count is made anew on every read (a lesson transcribed later changes it at once), and the
   table opens on «Только новое».
3. **Every text can be corrected in place** (2026-11-18): not only a word — its translation, its phrase, an
   example, what the example shows, a grammar rule and its examples, a topic. The builder's text is kept, the
   place shows what stood there before, one click puts it back.
4. **No dependence on an outside service** (2026-11-18): the package is shown by XtractManager itself; its
   data and the owner's edits lie on the owner's disk.
5. **The look** (2026-11-16, 2026-11-18): the whole width of a wide screen (about 3840 px) is used; tables
   have sorting, a filter and columns the owner can drag; every word has its meaning, its place in the
   recording and the phrase it was said in; a narrow window folds instead of scrolling sideways.
6. **Listening** follows from 5: a phrase is played from its place in the recording.
7. **This folder** (2026-11-18): the tool has its own instructions and prompts in the repository, so that a
   later chat can go on with it.

## Where things are

| Part | Place |
|---|---|
| Lists of a lesson, as built | `<packages folder>/<YYYY-MM-DD>/package.json` — written by the builder only |
| The owner's edits | `<packages folder>/<YYYY-MM-DD>/marks.json` — written by agent05 only |
| Packages folder | config key `LessonPackages:RootPath` of agent05; without it the folder `lesson-packages` **next to** the jobs folder |
| Model of the files, the view, the edit rule | `agent05-ui-control/API/Features/LessonPackages/Application/` — `LessonPackage.cs`, `LessonPackageViewBuilder.cs` |
| "Was this word said before" | `…/Application/LessonWordRules.cs` (the rule), `…/Infrastructure/LessonWordIndex.cs` (which transcripts are read, the cache) |
| Reading and writing the files | `…/Infrastructure/FileLessonPackageStore.cs` |
| HTTP | `agent05-ui-control/API/Controllers/LessonPackagesController.cs`; the lessons list tells which lesson has a package (`SessionsController.List`) |
| Screen | `agent05-ui-control/UI/src/components/` — `LessonPackageView.tsx` (header, tabs, grammar, topics, examples, player), `LessonPackageWords.tsx` (the words table), `EditableText.tsx` (a text edited in place), `LessonPageLinks.tsx` (the button) |
| Rows, filter, order, widths | `agent05-ui-control/UI/src/utils/lessonPackage.ts` |
| Words of the UI | `agent05-ui-control/UI/src/contexts/I18nContext.tsx`, keys `lessonPackage*` and `pkg*`, three languages |
| Tests | `agent05-ui-control/API.Tests/` — `LessonWordRulesTests.cs`, `LessonPackageViewBuilderTests.cs`, `LessonPackagesTests.cs`; `agent05-ui-control/UI/src/utils/lessonPackage.spec.ts` |
| Builder | not in the repository yet — see [BUILDER.md](BUILDER.md), "State" |

## Rules that must hold

1. **`package.json` is never rewritten by the service.** Everything that depends on other lessons or on the
   owner is counted on read: the ids, the edits put on, "said before", the audio track of a recording.
2. **An edit is kept by the id of its item, and the id is made of the builder's own text** (a word — of the
   word; a topic, a rule, an example — of a digest of its text). A package built anew keeps the edits of
   every item whose text did not change; the others are counted as stale and stay in the file. Do not make
   ids of positions in a list — also not for the examples inside a grammar theme, and not for two items of
   one name (they are told apart by the rest of their text, see CONTRACT.md).
3. **"Said before" is never stored.** It is counted from the transcripts in the job folders each time
   (cached in memory by the size and write time of the files).
4. **The recordings folder is only read.** Audio is streamed from it, nothing is written there.
5. **The packages folder is not inside the jobs folder**: every folder in the jobs folder is listed as a job.
6. **A file that cannot be read is never written over** (the request gets 409 and the file stays), and
   what a hand added to `marks.json` is kept by every write, also when the edit next to it is taken off.
7. **A request to a model costs money and needs the owner's word**: name the estimate first, the actual cost
   after. Building a package is such a request (see BUILDER.md for the price).
8. **The repository is public.** No line of a lesson, no name of a person, no audio, no address of a page of
   the owner in code, tests, docs or prompts. Tests use made-up words. The examples inside the prompts here
   are made up for the same reason.
9. **The job folders are the owner's.** Nothing of the package is written into them; applying a corrected
   phrase to a transcript would change job files and needs the owner's word.
10. Three languages of the UI (en, ru, es) for every new text; console logs in UTC; conventional commits
    with the agent scope.

## How to check a change

```bash
# agent05 API tests (from agent05-ui-control); add --artifacts-path <temp folder> while the API is running
dotnet test API.Tests --filter "FullyQualifiedName~LessonPackage|FullyQualifiedName~LessonWordRules"

# UI (from agent05-ui-control/UI)
npm run test -- lessonPackage
npx tsc -b && npm run lint
```

Then look at the screen: start agent05 and the UI (root `CLAUDE.md`, "Commands"), open a lesson that has a
package, and check by hand what the tests cannot — the table at full width and in a narrow window, an edit
saved and put back, a phrase played. The first read after agent05 starts is slow — about 20 s with 36
lessons: nearly all of it is the scan of the recordings folder and the tying of the jobs to its tracks (the
same work the lessons list does), the transcripts themselves are read in about 2 s, four lessons at a time
(agent05 logs the time). The next reads take a fraction of a second.

## How to change things

- **A new field of the package**: the builder writes it → `LessonPackage.cs` → the type in
  `lessonPackage.ts` → the screen → [CONTRACT.md](CONTRACT.md). A field the service does not know is skipped
  on read, so the builder may go first.
- **A field the owner may edit**: the list of fields of the item in `LessonPackageViewBuilder.Items` (name,
  how it is read and written) → `EditableText` at its place on the screen → a test → CONTRACT.md. The first
  field of the list names the item and cannot be left empty.
- **The rule "said before"**: `LessonWordRules` and its tests. The builder picks its candidates by the same
  rule in its own code; a change here that is not made there makes the two disagree (the screen is the one
  the owner sees).
- **A prompt**: change its file in `prompts/`, and write in BUILDER.md what was measured before and after.
  A prompt is checked only by a paid run — rule 7.
- **A new kind of list**: its model and ids first (rule 2), then the screen.

## State (2026-11-18) and what is next

Done: the screen with four tabs; the words table (only new by default, lists, search, recording, sorting,
columns dragged and remembered); every text edited in place and put back; a row taken out and returned; a
phrase played from the recording; "said before" counted against all earlier lessons; four lessons have a
package.

Not done, in the order it is worth doing:

1. **The builder in the repository** — see BUILDER.md. Until then a package is built by scripts that lie
   outside the repository and is converted to `package.json` by hand.
2. **A package built from the screen** — a button at a lesson with the estimate shown before the start
   (paid; rule 7).
3. **The weak spots of the lists** named in BUILDER.md.
4. **A corrected phrase written into the transcript** — rule 9.
5. `DELETE …/package/marks/{id}` exists, the screen does not use it yet (it puts fields back one by one).
