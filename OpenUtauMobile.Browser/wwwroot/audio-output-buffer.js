// 与 BrowserAudioBuffer.cs 共用布局；队列位于固定的 WASM 内存，计数单位为立体声帧。
export const Read = 0, Write = 1, Playing = 2, Ended = 3, Consumed = 4, Running = 5;
export const headerLength = 8;
export const capacity = 4096;
export const targetFrames = 2048;
