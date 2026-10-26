// Helpers for stories that run a real Web Worker.

type WorkerLike = {
  postMessage(message: unknown): void;
  terminate(): void;
  onmessage: unknown;
  onerror: unknown;
};

/**
 * Creates the worker of a definition once, waits for its first answer or error and terminates it.
 *
 * A module worker is fetched when it is created, which is after a story has rendered. The dev server transforms
 * every story in the background, and a worker script that is requested meanwhile waits behind that work. The wait
 * was 5 s on an idle machine and 8 s with a busy CPU, against the 10 s that a story gives a request. A story that
 * calls this in a loader pays the wait before it renders, where only the 120 s test timeout applies. The worker that
 * the story creates afterwards starts from the transformed script within milliseconds.
 */
export async function warmUpWorker(definition: { Ctor?: () => WorkerLike }, request: unknown): Promise<void> {
  const worker = definition.Ctor!();
  await new Promise<void>((resolve) => {
    worker.onmessage = () => resolve();
    worker.onerror = () => resolve();
    worker.postMessage({ id: 0, req: request });
  });
  worker.terminate();
}
