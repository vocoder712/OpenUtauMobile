// 主线程和音频线程共用的帧队列；每一代播放使用独立内存，避免重置时覆盖旧读指针。
import { Read, Write, Playing, Ended, Consumed, Requested, headerLength, capacity, targetFrames } from './audio-output-buffer.js';

class OpenUtauAudioProcessor extends AudioWorkletProcessor {
    constructor() {
        super();
        this.generation = 0;
        this.header = null;
        this.framesSinceReport = 0;
        this.port.onmessage = ({ data }) => {
            if (data.type === "configure") {
                this.generation = data.generation;
                this.header = new Int32Array(data.buffer, 0, headerLength);
                this.samples = new Float32Array(data.buffer, headerLength * 4);
                this.framesSinceReport = 0;
            }
        };
    }

    process(inputs, outputs) {
        const output = outputs[0];
        if (!output?.length || !this.header || !Atomics.load(this.header, Playing)) return true;
        const header = this.header;
        const read = Atomics.load(header, Read) >>> 0;
        const write = Atomics.load(header, Write) >>> 0;
        const available = (write - read) >>> 0;
        const count = Math.min(output[0].length, available);
        for (let frame = 0; frame < count; frame++) {
            const offset = ((read + frame) % capacity) * 2;
            output[0][frame] = this.samples[offset];
            if (output[1]) output[1][frame] = this.samples[offset + 1];
        }
        // 欠载补零不推进输入时钟；采样源返回的静音仍作为正常输入消费。
        Atomics.store(header, Read, (read + count) | 0);
        Atomics.add(header, Consumed, count);
        const remaining = available - count;
        if (remaining <= targetFrames / 2 && !Atomics.exchange(header, Requested, 1)) {
            this.port.postMessage({ type: "need", generation: this.generation });
        }
        this.framesSinceReport += output[0].length;
        if (remaining === 0 && Atomics.load(header, Ended)) {
            Atomics.store(header, Playing, 0);
            this.port.postMessage({ type: "ended", generation: this.generation });
        } else if (this.framesSinceReport >= sampleRate / 50) {
            this.framesSinceReport = 0;
            this.port.postMessage({ type: "position", generation: this.generation });
        }
        return true;
    }
}

registerProcessor("openutau-audio-output", OpenUtauAudioProcessor);
