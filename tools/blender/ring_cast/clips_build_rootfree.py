# Перенос руху таза власного кліпу в криву кореня (та сама логіка, що для готових кліпів).
import numpy as np
from clips_lib import *


def to_root(sk, lq, h, F):
    """Лишає таз над початковою точкою, шлях — у кореневій кривій (x, y, yaw)."""
    root = np.zeros((F, 3))
    root[:, :2] = h[:, :2] - h[0, :2]
    h2 = h.copy()
    h2[:, :2] = h[0, :2]
    return lq, h2, root
