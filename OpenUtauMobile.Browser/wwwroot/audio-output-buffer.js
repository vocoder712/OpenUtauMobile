// 两个线程共同使用的队列协议；索引与计数均以立体声帧为单位。
export const Read = 0, Write = 1, Playing = 2, Ended = 3, Consumed = 4, Requested = 5;
export const headerLength = 8;
export const capacity = 4096;
export const targetFrames = 2048;
