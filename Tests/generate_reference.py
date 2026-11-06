"""Generate private pose fixtures from local assets and an independent evaluator.

Usage: python Tests/generate_reference.py GAME_DATA KH1_EVDL_TOOLS OUTPUT_JSON
Reference: gaithern/KH1-EVDL-TOOLS, commit 5df63d2 (anim/).
Generated fixtures contain game data; keep them outside the repository.
"""
import json
from pathlib import Path
import struct
import sys
import tempfile

game, reference, output = map(Path, sys.argv[1:4])
sys.path.insert(0, str(reference.resolve() / 'anim'))
import kh1_mset as motion_reader
import kh1_pose as evaluator

fixtures = []
with tempfile.TemporaryDirectory() as temporary:
    for archive_name in ['lm01.ard', 'al01.ard', 'he01.ard', 'nm01.ard', 'nm03.ard', 'nm06.ard', 'pc01.ard']:
        archive = game / archive_name
        data = archive.read_bytes()
        offsets = struct.unpack_from('<33I', data, 4)
        names = [data[i:i + 32].split(b'\0')[0].decode('ascii')
                 for i in range(offsets[5], offsets[6], 32)]
        names = [Path(name).stem for name in names if not name.lower().endswith('.mset')]
        base = offsets[0]
        count = motion_reader.read_u32(data, base)
        slots = struct.unpack_from('<' + str(count + 1) + 'I', data, base + 4)
        models = []
        for index in range(count):
            slot = data[base + slots[index]:base + slots[index + 1]]
            if slot[128:132] != b'MOBJ':
                continue
            name = names[index] if index < len(names) else f'model_slot_{index}'
            model_path = Path(temporary) / 'sample.mdls'
            model_path.write_bytes(slot)
            model = evaluator.read_model(model_path)
            # Prop models can have no character info block. Ignore invalid
            # look-at indices rather than indexing unrelated bytes as a bone.
            look = model['look_at_bone'] if model['look_at_bone'] != -1 else model['look_at_bone_fallback']
            if not 0 < look <= model['bone_count']:
                model['look_at_bone'] = -1
                model['look_at_bone_fallback'] = 1
            poses = []
            clip = 0
            position = slot.find(b'MMTN')
            while position >= 0:
                table = position + motion_reader.read_u32(slot, position + 0x30)
                j = 0
                while motion_reader.read_u32(slot, table + j * 4) != 0xffffffff:
                    start = table + motion_reader.read_u32(slot, table + j * 4)
                    j += 1
                    motion = motion_reader.read_motion(slot, start)
                    if motion['bones'] != model['bone_count']:
                        continue
                    for frame in [0.0, 0.5, 1.0, motion['frames'] * 0.37, motion['frames'] * 0.8]:
                        # Cancel the reference's actor-space Y/Z reversal so
                        # both evaluators return unplaced MDLS-space poses.
                        actor = [1,0,0,0, 0,-1,0,0, 0,0,-1,0, 0,0,0,1]
                        pose = evaluator.pose_from_data(model, motion, frame, actor_matrix=actor)
                        basis = [-0.01, 0.01, 0.01, 1]
                        worlds = [[w[r * 4 + c] * basis[c] / basis[r]
                                   for r in range(4) for c in range(4)] for w in pose['world_matrices']]
                        poses.append(dict(clip=clip, frame=frame, worlds=worlds))
                    clip += 1
                position = slot.find(b'MMTN', position + 4)
            if poses:
                models.append(dict(name=name, poses=poses))
                print(archive_name, name, 'clips', clip, 'poses', len(poses))
        fixtures.append(dict(path=str(archive.resolve()), models=models))
output.write_text(json.dumps(fixtures, allow_nan=False), encoding='utf-8')
