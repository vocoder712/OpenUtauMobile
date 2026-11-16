// 后台生产者与音频线程直接共用 WASM 帧队列；主线程不参与持续供音。
import { Read, Write, Playing, Ended, Consumed, headerLength, capacity } from './audio-output-buffer.js';

class OpenUtauAudioProcessor extends AudioWorkletProcessor {
    constructor() {
        super();
        this.generation = 0;
        this.header = null;
        this.framesSinceReport = 0;
        this.port.onmessage = ({ data }) => {
            if (data.type === "configure") {
                const previousGeneration = this.generation;
                const hadBuffer = this.header !== null;
                this.generation = data.generation;
                this.header = data.buffer ? new Int32Array(data.buffer, data.address, headerLength) : null;
                this.samples = data.buffer ? new Float32Array(data.buffer, data.address + headerLength * 4, capacity * 2) : null;
                this.framesSinceReport = 0;
                // 消息和 process 在同一音频线程串行执行，确认之后不再访问旧地址。
                if (hadBuffer) this.port.postMessage({ type: "released", generation: previousGeneration });
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
        this.framesSinceReport += output[0].length;
        // 结束标记可能在本次写指针快照之后发布，须再次确认末块已经消费完。
        if (remaining === 0 && Atomics.load(header, Ended)
            && (Atomics.load(header, Write) >>> 0) === ((read + count) >>> 0)) {
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
