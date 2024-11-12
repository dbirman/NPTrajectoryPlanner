import numpy as np
import pandas as pd

# p0 = generate_multi_columns_probe(
#     num_columns=2, num_contact_per_column=8, xpitch=30, ypitch=46,
#     y_shift_per_column=[23, 0], contact_shapes='circle', 
#     contact_shape_params={'radius': 17.7/2})

labels = ['electrode', 'x', 'y', 'z', 'w', 'h', 'd', 'default', 'all']

n_probe = 64
n_shank = 16
pitch_shank = 200
n_col = 2
pitch_x = 30
pitch_y = 46
y_shift = 23
min_y = 52/2
w = 17.7/2
h = w
d = 0
default = 1
all = 1

electrodes = pd.DataFrame(np.zeros((n_probe, len(labels))), columns=labels)
idx = 0
for s in range(int(n_probe/n_shank)):
    for r in range(int(n_shank/n_col)):
        for c in range(n_col):
            if not c:
                row = [
                    idx, s*pitch_shank+pitch_x/2, r*pitch_y+min_y, 0, 
                    w, h, d, default, all]
            else:
                row = [
                    idx, s*pitch_shank-pitch_x/2, r*pitch_y+min_y+y_shift, 0, 
                    w, h, d, default, all]
            electrodes.iloc[idx] = row
            idx += 1

electrodes.to_csv('./a4x16.csv', index=False)