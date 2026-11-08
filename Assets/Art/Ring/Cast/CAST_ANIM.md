# Анімації персонажів «Рингу»: граф станів, кросфейди, шари, цикли

Згенеровано `tools/blender/ring_cast/cast_graph.py` з даних кліпів; руками не правити. За цим файлом збирається Animator у задачі 3.

## Скелет і кліпи

- Кістки: імена й ієрархія `mixamorig:` (Y Bot), корінь — `mixamorig:Hips`, без кінцевих `*_End` і `*4`. 52 кістки.
- Поза зв'язування — A-поза, бо вона менше ламає плечі; у Unity Avatar імпортується як Humanoid.
- Два набори кліпів: `RING_Cast_M.fbx` і `RING_Cast_F.fbx` (та сама ієрархія, жіноча манера рухів). 30 кадрів/с.
- Таз у кліпах стоїть на місці: зміщення знято в окрему криву (`root_motion`). Код рухає персонажа за цією кривою, після кліпу він стоїть там, де її кінець.

## Склад

| Літера | Персонаж | Стать | Зріст, м | Архетип | Реквізит |
|---|---|---|---|---|---|
| А | Богдан «Сталевий» Дуб | ч | 1.92 | Силач-чемпіон, герой | belt |
| Б | Матео «Колібрі» Рамос | ч | 1.85 | Летун-лучадор, герой | chair |
| Г | Олівер «Барон» Вінслоу | ч | 1.88 | Шоумен, лиходій | chair, kendo, case |
| Бос | Тарас «Молот» Гребінь | ч | 2.40 | Монстр, головний лиходій | — |
| В | Амара «Комета» Окафор | ж | 1.70 | Чемпіонка-летунка, героїня | mic |
| — | Мей «Вдова» Лін | ж | 1.68 | Технік, лиходійка | — |
| — | Новачка | ж | 1.66 | Новачка-бунтарка, героїня гравця | — |
| Д | Ліна Ворон | ж | 1.72 | Менеджерка й букерка, не борець | spray |

Зріст — ПЛЕЙСХОЛДЕР: у `docs/ring/gdd.md` чисел немає. Босс 2,40 м: пропорції ті самі, об'єм більший; листа розворотів у репозиторії немає.

## Стани

| Стан | Циклічний кліп | Опис |
|---|---|---|
| `idle` | `U_Idle_Loop` | Стоїть вільно |
| `walk` | `U_Walk_Loop` | Ходьба |
| `jog` | `Run_Jog_Loop` | Підтюпцем |
| `run` | `Run_Loop` | Біг по рингу |
| `stance` | `Ring_Stance_Loop` | Стійка захисту |
| `stance_walk` | `Move_Walk_Fwd_Loop` | Хода в стійці |
| `crouch` | `U_Crouch_Idle_Loop` | Присід |
| `kneel` | `Kneel_Idle_Loop` | На колінах |
| `sit_mat` | `Sit_Mat_Idle_Loop` | Сидить на маті |
| `lying_back` | `Lying_Back_Loop` | Лежить на спині |
| `lying_front` | `Lying_Front_Loop` | Лежить на животі |
| `corner_lean` | `Move_Corner_Lean_Loop` | Спирається в куті |
| `top_rope` | `Top_Rope_Perch_Loop` | На верхньому канаті |
| `promo` | `promo_idle` | Промо з мікрофоном |
| `entrance_walk` | `Entrance_Walk_Loop` | Вихід: хода |
| `held_stand` | `Held_Stand_Loop` | У захваті стоячи |
| `held_sit` | `Held_Sit_Loop` | У захваті сидячи |
| `held_kneel` | `Held_Kneel_Loop` | У захваті на колінах |

## Переходи (чоловічий набір; жіночий — у `graph.json`)

Кросфейд, с: рахується з різниці поз на стику. Де висота таза розходиться більш ніж на 0,25 м або кисті більш ніж на 0,7 м, стоїть клип-перехід, а не довгий кросфейд.

| Звідки | Куди | Клип-перехід | Кросфейд у нього, с | Кросфейд із нього, с |
|---|---|---|---|---|
| `idle` | `walk` | — | 0.24 | — |
| `walk` | `idle` | — | 0.24 | — |
| `walk` | `jog` | — | 0.18 | — |
| `jog` | `walk` | — | 0.18 | — |
| `jog` | `run` | — | 0.11 | — |
| `run` | `jog` | — | 0.11 | — |
| `idle` | `stance` | `Tr_Idle_To_Stance` | 0.07 | 0.07 |
| `stance` | `idle` | `Tr_Stance_To_Idle` | 0.07 | 0.07 |
| `stance` | `stance_walk` | — | 0.11 | — |
| `stance_walk` | `stance` | — | 0.11 | — |
| `stance` | `crouch` | `Tr_Stance_To_Crouch` | 0.07 | 0.07 |
| `crouch` | `stance` | `Tr_Crouch_To_Stance` | 0.07 | 0.07 |
| `crouch` | `kneel` | `Tr_Crouch_To_Kneel` | 0.07 | 0.07 |
| `kneel` | `crouch` | `Tr_Kneel_To_Crouch` | 0.07 | 0.07 |
| `stance` | `kneel` | `Tr_Stance_To_Kneel` | 0.07 | 0.07 |
| `kneel` | `stance` | `Tr_Kneel_To_Stance` | 0.07 | 0.07 |
| `kneel` | `sit_mat` | `Tr_Kneel_To_Sit` | 0.07 | 0.07 |
| `sit_mat` | `kneel` | `Tr_Sit_To_Kneel` | 0.07 | 0.07 |
| `stance` | `run` | `Run_Start` | 0.35 | 0.12 |
| `run` | `stance` | `Run_Stop` | 0.11 | 0.17 |
| `stance` | `lying_back` | `Bump_Back` | 0.07 | 0.07 |
| `lying_back` | `stance` | `GetUp_Back` | 0.07 | 0.07 |
| `stance` | `lying_front` | `Bump_Front` | 0.07 | 0.07 |
| `lying_front` | `lying_back` | `Roll_Front_To_Back` | 0.07 | 0.07 |
| `stance` | `corner_lean` | `Move_Corner_Enter` | 0.07 | 0.07 |
| `corner_lean` | `stance` | `Move_Corner_Exit` | 0.07 | 0.07 |
| `stance` | `top_rope` | `Corner_Climb_Top` | 0.26 | 0.08 |
| `top_rope` | `lying_front` | `Top_Dive_Crossbody` | 0.15 | 0.07 |
| `idle` | `promo` | `Tr_Idle_To_Promo` | 0.07 | 0.07 |
| `promo` | `idle` | `Tr_Promo_To_Idle` | 0.07 | 0.07 |
| `idle` | `entrance_walk` | `Entrance_Curtain_Face_Point` | 0.24 | 0.22 |
| `entrance_walk` | `stance` | `Entrance_Through_Ropes` | 0.23 | 0.31 |
| `stance` | `held_stand` | `Tr_To_Held_stand` | 0.07 | 0.07 |
| `held_stand` | `stance` | `Tr_From_Held_stand` | 0.07 | 0.07 |
| `kneel` | `held_kneel` | `Tr_To_Held_kneel` | 0.07 | 0.07 |
| `held_kneel` | `kneel` | `Tr_From_Held_kneel` | 0.07 | 0.07 |
| `sit_mat` | `held_sit` | `Tr_To_Held_sit` | 0.07 | 0.07 |
| `held_sit` | `sit_mat` | `Tr_From_Held_sit` | 0.07 | 0.07 |

## Дії зі стійки

Кліп має вузли початку й кінця. Парні кліпи `_A` і `_B` стартують разом; зсув і поворот B відносно A — у `clips_x.json` (`offset`, `rot`).

| Кліп | До вузла | Тривалість, с | Рух, м | Контакт, с |
|---|---|---|---|---|
| `Bodyslam_A` | — | 2.80 | 0.06 | — |
| `Bodyslam_B` | lying_back | 2.80 | 0.40 | — |
| `Bump_Back` | lying_back | 1.30 | 0.40 | — |
| `Bump_Back_Sell_Arch` | lying_back | 3.40 | 0.40 | — |
| `Bump_Back_Sell_Back` | lying_back | 3.40 | 0.40 | — |
| `Bump_Back_Sell_Head` | lying_back | 3.40 | 0.40 | — |
| `Bump_Back_Sell_KO` | lying_back | 3.40 | 0.40 | — |
| `Bump_Back_Sell_Reach` | lying_back | 3.40 | 0.40 | — |
| `Bump_Front` | lying_front | 1.30 | 0.45 | — |
| `Bump_Front_Sell_Crawl` | lying_front | 3.40 | 0.45 | — |
| `Bump_Front_Sell_KO` | lying_front | 3.40 | 0.45 | — |
| `Bump_Front_Sell_Knees` | lying_front | 3.40 | 0.45 | — |
| `Bump_Front_Sell_PushUp` | lying_front | 3.40 | 0.45 | — |
| `Bump_Front_Sell_RollBack` | lying_back | 3.40 | 0.45 | — |
| `Bump_Side` | — | 1.30 | 0.55 | — |
| `Bump_Side_Sell_BackArch` | lying_back | 3.40 | 0.55 | — |
| `Bump_Side_Sell_Curl` | lying_back | 3.40 | 0.55 | — |
| `Bump_Side_Sell_Ribs` | lying_back | 3.40 | 0.55 | — |
| `Bump_Side_Sell_RollFront` | lying_front | 3.40 | 0.55 | — |
| `Bump_Side_Sell_Writhe` | lying_back | 3.40 | 0.55 | — |
| `Center_BigBoot_B` | lying_back | 2.60 | 0.40 | 0.68 |
| `Center_BigBoot_Stagger_B` | stance | 2.60 | 0.45 | 0.62 |
| `Center_Clothesline_B` | lying_back | 2.60 | 0.40 | 0.68 |
| `Center_Clothesline_Flip_B` | lying_front | 2.60 | 0.60 | 0.68 |
| `Center_Duck_B` | stance | 2.60 | 0.00 | — |
| `Center_ForearmShiver_B` | stance | 2.60 | 0.35 | 0.62 |
| `Center_Headbutt_B` | stance | 2.60 | 0.35 | 0.62 |
| `Center_Lariat_Heavy_B` | lying_back | 2.60 | 0.40 | 0.68 |
| `Center_Lariat_Left_B` | — | 2.60 | 0.55 | 0.68 |
| `Center_RunningElbow_B` | — | 2.60 | 0.55 | 0.68 |
| `Center_RunningKnee_B` | lying_back | 2.60 | 0.40 | 0.68 |
| `Center_ShoulderBlock_B` | lying_back | 2.60 | 0.40 | 0.68 |
| `Center_ShoulderBlock_Heavy_B` | lying_back | 2.60 | 0.40 | 0.68 |
| `Center_ShoulderBlock_NoSell_B` | stance | 2.60 | 0.35 | 0.62 |
| `Center_Sidestep_B` | stance | 2.60 | 0.36 | — |
| `Chain_ArmWringer_A` | — | 2.20 | 0.12 | — |
| `Chain_ArmWringer_B` | — | 2.20 | 0.13 | — |
| `Chain_ArmWringer_Kneel_A` | — | 2.20 | 0.12 | — |
| `Chain_ArmWringer_Kneel_B` | — | 2.20 | 0.13 | — |
| `Chain_FrontFacelock_A` | — | 2.40 | 0.20 | — |
| `Chain_FrontFacelock_B` | — | 2.40 | 0.31 | — |
| `Chain_FrontFacelock_Clubs_A` | — | 2.40 | 0.20 | — |
| `Chain_FrontFacelock_Clubs_B` | — | 2.40 | 0.31 | — |
| `Chain_FrontFacelock_Knees_A` | — | 2.40 | 0.20 | — |
| `Chain_FrontFacelock_Knees_B` | — | 2.40 | 0.31 | — |
| `Chain_Hammerlock_A` | — | 2.40 | 0.22 | — |
| `Chain_Hammerlock_B` | — | 2.40 | 0.53 | — |
| `Chain_Hammerlock_Lift_A` | — | 2.40 | 0.22 | — |
| `Chain_Hammerlock_Lift_B` | — | 2.40 | 0.53 | — |
| `Chain_Headlock_A` | — | 2.40 | 0.20 | — |
| `Chain_Headlock_B` | — | 2.40 | 1.01 | — |
| `Chain_Headlock_Crank_A` | — | 2.40 | 0.20 | — |
| `Chain_Headlock_Crank_B` | — | 2.40 | 1.01 | — |
| `Chain_Headlock_Punches_A` | — | 2.40 | 0.20 | — |
| `Chain_Headlock_Punches_B` | — | 2.40 | 1.01 | — |
| `Chain_ShoveOff_A` | — | 1.40 | 0.10 | — |
| `Chain_ShoveOff_B` | — | 1.40 | 0.69 | — |
| `Chain_Waistlock_A` | — | 2.40 | 0.97 | — |
| `Chain_Waistlock_B` | — | 2.40 | 0.05 | — |
| `Chain_Waistlock_Elbows_A` | — | 2.40 | 0.97 | — |
| `Chain_Waistlock_Elbows_B` | — | 2.40 | 0.05 | — |
| `Chain_Wristlock_A` | — | 2.20 | 0.10 | — |
| `Chain_Wristlock_B` | — | 2.20 | 0.06 | — |
| `Chain_Wristlock_Spin_A` | — | 2.20 | 0.10 | — |
| `Chain_Wristlock_Spin_B` | — | 2.20 | 0.06 | — |
| `Corner_Climb_Top` | top_rope | 2.60 | 0.00 | — |
| `Corner_Strike` | stance | 1.50 | 0.00 | 0.64 |
| `Cover_A` | — | 3.00 | 0.05 | — |
| `Forearm_Strike_A` | stance | 2.00 | 0.00 | 0.42 |
| `Forearm_Strike_B` | stance | 2.00 | 0.00 | 0.42 |
| `Lockup_A` | — | 2.40 | 0.25 | 0.62 |
| `Lockup_B` | — | 2.40 | 0.25 | 0.62 |
| `Lockup_Cautious_A` | — | 2.77 | 0.25 | — |
| `Lockup_Cautious_B` | — | 2.77 | 0.25 | — |
| `Lockup_Drive_Back_Ropes_A` | — | 2.40 | 1.06 | — |
| `Lockup_Drive_Back_Ropes_B` | — | 2.40 | 1.35 | — |
| `Lockup_Exchange_A` | — | 2.40 | 0.36 | — |
| `Lockup_Exchange_B` | — | 2.40 | 0.20 | — |
| `Lockup_Fast_A` | — | 2.17 | 0.25 | — |
| `Lockup_Fast_B` | — | 2.17 | 0.25 | — |
| `Lockup_Heavy_A` | — | 2.40 | 0.56 | — |
| `Lockup_Heavy_B` | — | 2.40 | 0.30 | — |
| `Lockup_Low_A` | — | 2.40 | 0.25 | — |
| `Lockup_Low_B` | — | 2.40 | 0.25 | — |
| `Lockup_Power_A_A` | — | 2.40 | 0.81 | — |
| `Lockup_Power_A_B` | — | 2.40 | 0.52 | — |
| `Lockup_Power_B_A` | — | 2.40 | 0.52 | — |
| `Lockup_Power_B_B` | — | 2.40 | 0.81 | — |
| `Lockup_Ref_Break_A` | — | 2.40 | 0.05 | — |
| `Lockup_Ref_Break_B` | — | 2.40 | 0.05 | — |
| `Lockup_Shove_Off_A` | — | 2.40 | 0.05 | — |
| `Lockup_Shove_Off_B` | — | 2.40 | 0.05 | — |
| `Lockup_Side_Walk_A` | — | 2.40 | 0.91 | — |
| `Lockup_Side_Walk_B` | — | 2.40 | 0.52 | — |
| `Lockup_Side_Walk_R_A` | — | 2.40 | 0.52 | — |
| `Lockup_Side_Walk_R_B` | — | 2.40 | 0.91 | — |
| `Lockup_Stalemate_A` | — | 2.40 | 0.25 | — |
| `Lockup_Stalemate_B` | — | 2.40 | 0.25 | — |
| `Lockup_To_Ropes_A` | — | 2.40 | 1.35 | — |
| `Lockup_To_Ropes_B` | — | 2.40 | 1.06 | — |
| `Move_Corner_Enter` | corner_lean | 1.30 | 0.48 | — |
| `Move_Rope_Rebound` | — | 1.73 | 0.34 | — |
| `Move_Turn_180` | stance | 1.50 | 0.00 | — |
| `Move_Turn_L90` | stance | 1.10 | 0.00 | — |
| `Move_Turn_R90` | stance | 1.10 | 0.00 | — |
| `Ref_Raise_Hand_B` | — | 2.60 | 0.00 | — |
| `Whip_Corner_A` | stance | 3.03 | 0.00 | — |
| `Whip_Corner_B` | corner_lean | 3.03 | 2.29 | — |
| `Whip_Corner_Chest_A` | stance | 3.03 | 0.00 | — |
| `Whip_Corner_Chest_B` | stance | 3.03 | 2.24 | — |
| `Whip_Corner_Fast_A` | stance | 2.50 | 0.00 | — |
| `Whip_Corner_Fast_B` | corner_lean | 2.50 | 2.29 | — |
| `Whip_Corner_Left_A` | stance | 3.03 | 0.00 | — |
| `Whip_Corner_Left_B` | corner_lean | 3.03 | 2.29 | — |
| `Whip_Corner_Resist_A` | stance | 3.47 | 0.00 | — |
| `Whip_Corner_Resist_B` | corner_lean | 3.47 | 2.29 | — |
| `Whip_Corner_Slump_A` | stance | 2.73 | 0.00 | — |
| `Whip_Corner_Slump_B` | — | 2.73 | 2.34 | — |
| `Whip_Corner_Two_Hand_A` | stance | 3.03 | 0.00 | — |
| `Whip_Corner_Two_Hand_B` | corner_lean | 3.03 | 2.29 | — |
| `Whip_Ropes_A` | stance | 2.03 | 0.00 | — |
| `Whip_Ropes_B` | — | 2.03 | 3.11 | — |
| `Whip_Ropes_Fast_A` | stance | 1.73 | 0.00 | — |
| `Whip_Ropes_Fast_B` | — | 1.73 | 3.12 | — |
| `Whip_Ropes_Left_A` | stance | 2.03 | 0.00 | — |
| `Whip_Ropes_Left_B` | — | 2.03 | 3.11 | — |
| `Whip_Ropes_Resist_A` | stance | 2.47 | 0.00 | — |
| `Whip_Ropes_Resist_B` | — | 2.47 | 3.07 | — |
| `Whip_Ropes_Stumble_A` | stance | 2.23 | 0.00 | — |
| `Whip_Ropes_Stumble_B` | — | 2.23 | 3.05 | — |
| `Whip_Ropes_Tired_A` | stance | 2.53 | 0.00 | — |
| `Whip_Ropes_Tired_B` | — | 2.53 | 3.09 | — |
| `Whip_Ropes_Two_Hand_A` | stance | 2.03 | 0.00 | — |
| `Whip_Ropes_Two_Hand_B` | — | 2.03 | 3.11 | — |
| `Whip_Short_Arm_A` | stance | 2.33 | 0.00 | — |
| `Whip_Short_Arm_B` | lying_back | 2.33 | 0.76 | — |
| `case_swing` | stance | 1.90 | 0.00 | 0.96 |
| `chair_swing` | stance | 1.90 | 0.00 | 0.96 |
| `kendo_swing` | stance | 1.60 | 0.00 | 0.87 |

## Маски шарів

- `full`: 52 кісток
- `upper_body`: 43 кісток (від `Spine` угору; таз і ноги лишаються базовому шару)
- `right_arm`: 19 кісток
- `left_arm`: 19 кісток
- `arms`: 38 кісток
- `lower_body`: 9 кісток

Шари: базовий — ноги й таз (ходьба, стійка, idle); верхній — дія руками за маскою `upper_body` або `right_arm`; реакції — адитивні (`AdditiveAnimationBlendMode` у three.js, Additive layer в Animator; опорний кадр — нульовий). Поза в захваті — шар `upper_body` (`Layer_Held_Arms_Loop`).

## Цикли

Перший кадр дорівнює останньому; кадр 0 локомоції — приземлення лівої стопи, тому при бленді ноги не плутаються. Швидкість — у м/с, код рухає персонажа саме так, тоді стопи не ковзають.

| Кліп | Тривалість, с | Швидкість, м/с | Фаза правої стопи |
|---|---|---|---|
| `Entrance_Corner_Pose` | 3.00 | 0.00 | — |
| `Entrance_Walk_Bouncy_Loop` | 0.93 | 1.13 | 0.39 |
| `Entrance_Walk_Focused_Loop` | 1.07 | 0.93 | 0.41 |
| `Entrance_Walk_Heavy_Loop` | 1.30 | 0.71 | 0.64 |
| `Entrance_Walk_Limp_Loop` | 1.47 | 0.55 | 0.95 |
| `Entrance_Walk_Loop` | 1.00 | 1.00 | 0.93 |
| `Entrance_Walk_Strut_Loop` | 1.20 | 0.83 | 0.22 |
| `Held_Kneel_Loop` | 3.20 | 0.00 | — |
| `Held_Sit_Loop` | 3.20 | 0.00 | — |
| `Held_Stand_Loop` | 3.20 | 0.00 | — |
| `Idle_Hurt_Body_Loop` | 2.40 | 0.00 | — |
| `Idle_Hurt_Head_Loop` | 2.60 | 0.00 | — |
| `Idle_Hurt_Legs_Loop` | 2.60 | 0.00 | — |
| `Idle_Hype_Loop` | 2.00 | 0.00 | — |
| `Idle_Tired_Loop` | 2.40 | 0.00 | — |
| `Kneel_Idle_Loop` | 3.20 | 0.00 | — |
| `Layer_Held_Arms_Loop` | 3.20 | 0.00 | — |
| `Lying_Back_Loop` | 4.00 | 0.00 | — |
| `Lying_Front_Loop` | 4.00 | 0.00 | — |
| `Move_Circle_L_Loop` | 1.00 | 0.36 | 0.07 |
| `Move_Circle_R_Loop` | 1.00 | 0.36 | 0.40 |
| `Move_Corner_Lean_Loop` | 3.00 | 0.00 | — |
| `Move_Run_Loop` | 0.67 | 3.94 | 0.40 |
| `Move_Walk_Back_Loop` | 1.00 | 0.36 | 0.93 |
| `Move_Walk_Fwd_Loop` | 1.00 | 0.36 | 0.67 |
| `Promo_Mic_Stare_Loop` | 3.00 | 0.00 | — |
| `Promo_Mic_Talk_Loop` | 4.00 | 0.00 | — |
| `Ref_Ask_Submit_Loop` | 2.00 | 0.00 | — |
| `Ref_Count_Outside_Loop` | 2.00 | 0.00 | — |
| `Ref_Idle_Loop` | 3.20 | 0.00 | — |
| `Ref_Walk_Loop` | 1.00 | 1.00 | 0.93 |
| `Ring_Stance_Loop` | 2.40 | 0.00 | — |
| `Run_Angry_Loop` | 0.60 | 4.67 | 0.44 |
| `Run_Backpedal_Loop` | 0.70 | 2.00 | 0.48 |
| `Run_Choppy_Loop` | 0.50 | 3.20 | 0.53 |
| `Run_Heavy_Loop` | 0.80 | 2.75 | 0.50 |
| `Run_Hurt_Body_Loop` | 0.83 | 2.26 | 0.52 |
| `Run_Jog_Loop` | 0.80 | 2.50 | 0.50 |
| `Run_Light_Loop` | 0.57 | 3.93 | 0.53 |
| `Run_Limp_Loop` | 0.87 | 2.09 | 0.50 |
| `Run_Loop` | 0.67 | 3.94 | 0.50 |
| `Run_Low_Loop` | 0.63 | 3.75 | 0.47 |
| `Run_Showoff_Loop` | 0.70 | 3.43 | 0.52 |
| `Run_Sprint_Loop` | 0.57 | 6.07 | 0.47 |
| `Run_Technic_Loop` | 0.67 | 3.64 | 0.40 |
| `Run_Tired_Loop` | 0.83 | 2.14 | 0.52 |
| `Sit_Mat_Idle_Loop` | 3.20 | 0.00 | — |
| `Top_Rope_Perch_Loop` | 3.00 | 0.00 | — |
| `U_Crouch_Fwd_Loop` | 2.00 | 0.80 | 0.13 |
| `U_Crouch_Idle_Loop` | 2.93 | 0.00 | — |
| `U_Dance_Loop` | 1.00 | 0.00 | — |
| `U_Idle_FoldArms_Loop` | 2.50 | 0.00 | — |
| `U_Idle_Loop` | 2.50 | 0.00 | — |
| `U_Idle_No_Loop` | 2.50 | 0.00 | — |
| `U_Idle_TalkingPhone_Loop` | 2.93 | 0.00 | — |
| `U_Idle_Talking_Loop` | 2.93 | 0.00 | — |
| `U_Jog_Fwd_Loop` | 0.93 | 5.94 | 0.50 |
| `U_Jump_Loop` | 2.50 | 0.00 | — |
| `U_NinjaJump_Idle_Loop` | 2.00 | 0.00 | — |
| `U_Push_Loop` | 2.67 | 0.00 | — |
| `U_Sitting_Idle_Loop` | 1.67 | 0.00 | — |
| `U_Sitting_Talking_Loop` | 2.93 | 0.00 | — |
| `U_Slide_Loop` | 2.00 | 0.00 | — |
| `U_Sprint_Loop` | 0.67 | 7.00 | 0.55 |
| `U_Walk_Formal_Loop` | 1.33 | 1.05 | 0.50 |
| `U_Walk_Loop` | 1.33 | 1.10 | 0.50 |
| `belt_hold` | 3.00 | 0.00 | — |
| `belt_walk_Loop` | 1.33 | 1.10 | 0.50 |
| `case_idle_Loop` | 3.00 | 0.00 | — |
| `case_walk_Loop` | 1.33 | 1.10 | 0.50 |
| `chair_idle_Loop` | 3.00 | 0.00 | — |
| `chair_walk_Loop` | 1.33 | 1.10 | 0.50 |
| `kendo_idle_Loop` | 3.00 | 0.00 | — |
| `kendo_walk_Loop` | 1.33 | 1.10 | 0.50 |
| `promo_idle` | 4.00 | 0.00 | — |
| `spray_idle_Loop` | 3.00 | 0.00 | — |
| `spray_water` | 2.00 | 0.00 | — |

## Кліпи зі зміщенням

| Кліп | Зсув x, м | Зсув y (−y уперед), м | Поворот, рад |
|---|---|---|---|
| `Bodyslam_A` | 0.00 | -0.06 | 0.00 |
| `Bodyslam_B` | 0.00 | 0.40 | 1.57 |
| `Bump_Back` | 0.00 | 0.40 | 0.00 |
| `Bump_Back_Sell_Arch` | 0.00 | 0.40 | 0.00 |
| `Bump_Back_Sell_Back` | 0.00 | 0.40 | 0.00 |
| `Bump_Back_Sell_Head` | 0.00 | 0.40 | 0.00 |
| `Bump_Back_Sell_KO` | 0.00 | 0.40 | 0.00 |
| `Bump_Back_Sell_Reach` | 0.00 | 0.40 | 0.00 |
| `Bump_Front` | 0.00 | -0.45 | 0.00 |
| `Bump_Front_Sell_Crawl` | 0.00 | -0.45 | 0.00 |
| `Bump_Front_Sell_KO` | 0.00 | -0.45 | 0.00 |
| `Bump_Front_Sell_Knees` | 0.00 | -0.45 | 0.00 |
| `Bump_Front_Sell_PushUp` | 0.00 | -0.45 | 0.00 |
| `Bump_Front_Sell_RollBack` | 0.00 | -0.45 | -3.14 |
| `Bump_Side` | -0.38 | 0.40 | 0.81 |
| `Bump_Side_Sell_BackArch` | -0.38 | 0.40 | 0.00 |
| `Bump_Side_Sell_Curl` | -0.38 | 0.40 | 0.00 |
| `Bump_Side_Sell_Ribs` | -0.38 | 0.40 | 0.00 |
| `Bump_Side_Sell_RollFront` | -0.38 | 0.40 | 3.14 |
| `Bump_Side_Sell_Writhe` | -0.38 | 0.40 | 0.00 |
| `Center_BigBoot_A` | 0.00 | -1.36 | 0.00 |
| `Center_BigBoot_B` | 0.00 | 0.40 | 0.00 |
| `Center_BigBoot_Stagger_A` | 0.00 | -1.36 | 0.00 |
| `Center_BigBoot_Stagger_B` | 0.00 | 0.45 | 0.00 |
| `Center_Clothesline_A` | 0.00 | -3.36 | 0.00 |
| `Center_Clothesline_B` | 0.00 | 0.40 | 0.00 |
| `Center_Clothesline_Flip_A` | 0.00 | -3.36 | 0.00 |
| `Center_Clothesline_Flip_B` | 0.00 | 0.60 | 0.00 |
| `Center_Duck_A` | 0.00 | -3.36 | 0.00 |
| `Center_ForearmShiver_A` | 0.00 | -1.36 | 0.00 |
| `Center_ForearmShiver_B` | 0.00 | 0.35 | 0.00 |
| `Center_Headbutt_A` | 0.00 | -1.36 | 0.00 |
| `Center_Headbutt_B` | 0.00 | 0.35 | 0.00 |
| `Center_Lariat_Heavy_A` | 0.00 | -3.70 | -0.26 |
| `Center_Lariat_Heavy_B` | 0.00 | 0.40 | 0.00 |
| `Center_Lariat_Left_A` | 0.00 | -3.36 | 0.00 |
| `Center_Lariat_Left_B` | -0.38 | 0.40 | 0.81 |
| `Center_RunningElbow_A` | 0.00 | -3.36 | 0.00 |
| `Center_RunningElbow_B` | -0.38 | 0.40 | 0.81 |
| `Center_RunningKnee_A` | 0.00 | -1.36 | 0.00 |
| `Center_RunningKnee_B` | 0.00 | 0.40 | 0.00 |
| `Center_ShoulderBlock_A` | 0.00 | -3.36 | 0.00 |
| `Center_ShoulderBlock_B` | 0.00 | 0.40 | 0.00 |
| `Center_ShoulderBlock_Heavy_A` | 0.00 | -3.70 | -0.26 |
| `Center_ShoulderBlock_Heavy_B` | 0.00 | 0.40 | 0.00 |
| `Center_ShoulderBlock_NoSell_A` | 0.00 | -0.90 | 0.00 |
| `Center_ShoulderBlock_NoSell_B` | 0.00 | 0.35 | 0.00 |
| `Center_ShoulderTackle_A` | 0.00 | -3.36 | 0.00 |
| `Center_ShoulderTackle_B` | 0.00 | -1.15 | 0.00 |
| `Center_Sidestep_A` | 0.00 | -3.36 | 0.00 |
| `Center_Sidestep_B` | 0.36 | -0.00 | 0.00 |
| `Chain_ArmWringer_A` | -0.08 | 0.08 | 0.00 |
| `Chain_ArmWringer_B` | 0.05 | 0.12 | 0.00 |
| `Chain_ArmWringer_Kneel_A` | -0.08 | 0.08 | 0.00 |
| `Chain_ArmWringer_Kneel_B` | 0.05 | 0.12 | 0.00 |
| `Chain_FrontFacelock_A` | -0.16 | 0.11 | 0.00 |
| `Chain_FrontFacelock_B` | 0.09 | -0.30 | 0.00 |
| `Chain_FrontFacelock_Clubs_A` | -0.16 | 0.11 | 0.00 |
| `Chain_FrontFacelock_Clubs_B` | 0.09 | -0.30 | 0.00 |
| `Chain_FrontFacelock_Knees_A` | -0.16 | 0.11 | 0.00 |
| `Chain_FrontFacelock_Knees_B` | 0.09 | -0.30 | 0.00 |
| `Chain_Hammerlock_A` | -0.18 | -0.13 | 0.00 |
| `Chain_Hammerlock_B` | -0.08 | -0.53 | -3.14 |
| `Chain_Hammerlock_Lift_A` | -0.18 | -0.13 | 0.00 |
| `Chain_Hammerlock_Lift_B` | -0.08 | -0.53 | -3.14 |
| `Chain_Headlock_A` | -0.16 | 0.11 | 0.00 |
| `Chain_Headlock_B` | 0.48 | -0.89 | -1.57 |
| `Chain_Headlock_Crank_A` | -0.16 | 0.11 | 0.00 |
| `Chain_Headlock_Crank_B` | 0.48 | -0.89 | -1.57 |
| `Chain_Headlock_Punches_A` | -0.16 | 0.11 | 0.00 |
| `Chain_Headlock_Punches_B` | 0.48 | -0.89 | -1.57 |
| `Chain_ShoveOff_A` | -0.10 | -0.03 | 0.00 |
| `Chain_ShoveOff_B` | -0.04 | 0.69 | 0.00 |
| `Chain_Waistlock_A` | -0.67 | -0.70 | 1.57 |
| `Chain_Waistlock_B` | 0.00 | -0.05 | -1.57 |
| `Chain_Waistlock_Elbows_A` | -0.67 | -0.70 | 1.57 |
| `Chain_Waistlock_Elbows_B` | 0.00 | -0.05 | -1.57 |
| `Chain_Wristlock_A` | -0.10 | -0.01 | 0.00 |
| `Chain_Wristlock_B` | 0.02 | 0.05 | 0.00 |
| `Chain_Wristlock_Spin_A` | -0.10 | -0.01 | 0.00 |
| `Chain_Wristlock_Spin_B` | 0.02 | 0.05 | 0.00 |
| `Cover_A` | -0.05 | -0.01 | 0.00 |
| `Entrance_Climb_Steps` | 0.00 | -1.82 | 0.00 |
| `Entrance_Pose_Turnbuckle_Arms` | 0.00 | -0.08 | 0.00 |
| `Entrance_Pose_Turnbuckle_Point` | 0.00 | -0.08 | 0.00 |
| `Entrance_Roll_In` | 0.00 | -1.48 | 0.00 |
| `Entrance_Through_Ropes` | 0.00 | -0.86 | 0.00 |
| `Entrance_Vault_Over_Top` | 0.00 | -0.90 | 0.00 |
| `Lockup_A` | 0.20 | -0.15 | 0.00 |
| `Lockup_B` | 0.20 | -0.15 | 0.00 |
| `Lockup_Cautious_A` | 0.20 | -0.15 | 0.00 |
| `Lockup_Cautious_B` | 0.20 | -0.15 | 0.00 |
| `Lockup_Drive_Back_Ropes_A` | 0.20 | 1.04 | 0.00 |
| `Lockup_Drive_Back_Ropes_B` | 0.20 | -1.34 | 0.00 |
| `Lockup_Exchange_A` | 0.20 | -0.30 | 0.00 |
| `Lockup_Exchange_B` | 0.20 | 0.00 | 0.00 |
| `Lockup_Fast_A` | 0.20 | -0.15 | 0.00 |
| `Lockup_Fast_B` | 0.20 | -0.15 | 0.00 |
| `Lockup_Heavy_A` | 0.20 | -0.52 | 0.00 |
| `Lockup_Heavy_B` | 0.20 | 0.22 | 0.00 |
| `Lockup_Low_A` | 0.20 | -0.15 | 0.00 |
| `Lockup_Low_B` | 0.20 | -0.15 | 0.00 |
| `Lockup_Power_A_A` | 0.20 | -0.78 | 0.00 |
| `Lockup_Power_A_B` | 0.20 | 0.48 | 0.00 |
| `Lockup_Power_B_A` | 0.20 | 0.48 | 0.00 |
| `Lockup_Power_B_B` | 0.20 | -0.78 | 0.00 |
| `Lockup_Ref_Break_A` | 0.00 | -0.05 | 0.00 |
| `Lockup_Ref_Break_B` | 0.00 | -0.05 | 0.00 |
| `Lockup_Shove_Off_A` | 0.00 | -0.05 | 0.00 |
| `Lockup_Shove_Off_B` | 0.00 | -0.05 | 0.00 |
| `Lockup_Side_Walk_A` | 0.90 | -0.15 | 0.00 |
| `Lockup_Side_Walk_B` | -0.50 | -0.15 | 0.00 |
| `Lockup_Side_Walk_R_A` | -0.50 | -0.15 | 0.00 |
| `Lockup_Side_Walk_R_B` | 0.90 | -0.15 | 0.00 |
| `Lockup_Stalemate_A` | 0.20 | -0.15 | 0.00 |
| `Lockup_Stalemate_B` | 0.20 | -0.15 | 0.00 |
| `Lockup_To_Ropes_A` | 0.20 | -1.34 | 0.00 |
| `Lockup_To_Ropes_B` | 0.20 | 1.04 | 0.00 |
| `Move_Corner_Enter` | 0.00 | 0.48 | 0.00 |
| `Move_Corner_Exit` | 0.00 | -0.48 | 0.00 |
| `Move_Rope_Rebound` | -0.00 | -0.34 | 6.28 |
| `Move_Turn_180` | 0.00 | 0.00 | 3.14 |
| `Move_Turn_L90` | 0.00 | 0.00 | 1.57 |
| `Move_Turn_R90` | 0.00 | 0.00 | -1.57 |
| `Ref_Back_Up` | 0.00 | 0.28 | 0.00 |
| `Ref_Kneel_Down` | 0.00 | 0.20 | 0.00 |
| `Ref_Kneel_Up` | 0.00 | -0.20 | 0.00 |
| `Ref_Knocked_Down` | 0.00 | 0.40 | 0.00 |
| `Roll_Front_To_Back` | 0.00 | -0.00 | -3.14 |
| `Rope_Back_L` | 0.00 | 1.18 | -3.15 |
| `Rope_Back_Low` | 0.00 | -0.01 | -3.21 |
| `Rope_Back_R` | 0.00 | 1.18 | 3.14 |
| `Rope_Back_R_Fast` | 0.00 | 0.36 | 3.01 |
| `Rope_Back_Stop` | 0.00 | -0.25 | -1.69 |
| `Rope_Deep` | 0.00 | 1.24 | -3.15 |
| `Rope_Duck_Return` | 0.00 | 1.18 | -3.15 |
| `Rope_Elbow_Hook` | 0.00 | 1.18 | -3.15 |
| `Rope_Fast` | 0.00 | 0.36 | -3.28 |
| `Rope_Heavy` | 0.00 | 0.85 | -3.17 |
| `Rope_Hesitate` | 0.00 | 1.42 | -3.19 |
| `Rope_Light` | 0.00 | 0.74 | -3.17 |
| `Rope_Roar` | 0.00 | 1.42 | -3.19 |
| `Rope_Slow_Tired` | 0.00 | 0.52 | -3.19 |
| `Rope_Stop_Lean` | 0.00 | -0.25 | -1.69 |
| `Run_Start` | 0.00 | -2.48 | 0.00 |
| `Run_Stop` | 0.00 | -1.24 | 0.00 |
| `Top_Dive_Crossbody` | 0.00 | -1.55 | 0.00 |
| `Top_Dive_Elbow` | 0.00 | -1.55 | 0.00 |
| `U_ClimbUp_1m_RM` | 0.00 | -1.69 | 0.00 |
| `U_Hit_Chest` | 0.00 | 0.03 | 0.00 |
| `U_Jump_Land` | 0.03 | -0.00 | 0.00 |
| `U_Jump_Start` | -0.03 | -0.08 | 0.00 |
| `U_LayToIdle` | 0.00 | -0.15 | -0.23 |
| `U_NinjaJump_Land` | 0.04 | 0.05 | 0.00 |
| `U_NinjaJump_Start` | -0.04 | -0.13 | 0.00 |
| `U_Punch_Enter` | 0.01 | -0.06 | 0.00 |
| `U_Roll` | 0.02 | -0.10 | 0.00 |
| `U_Sitting_Enter` | -0.00 | 0.25 | 0.23 |
| `U_Sitting_Exit` | 0.00 | -0.25 | -0.23 |
| `U_Slide_Exit` | 0.00 | 0.07 | 0.00 |
| `U_Slide_Start` | -0.00 | -0.07 | 0.00 |
| `Whip_Corner_A` | 0.00 | 0.00 | -1.31 |
| `Whip_Corner_B` | 0.75 | -2.17 | 3.14 |
| `Whip_Corner_Chest_A` | 0.00 | 0.00 | -1.31 |
| `Whip_Corner_Chest_B` | 0.75 | -2.11 | 0.00 |
| `Whip_Corner_Fast_A` | 0.00 | 0.00 | -1.31 |
| `Whip_Corner_Fast_B` | 0.75 | -2.17 | 3.14 |
| `Whip_Corner_Left_A` | 0.00 | 0.00 | 1.31 |
| `Whip_Corner_Left_B` | -0.75 | -2.17 | 3.14 |
| `Whip_Corner_Resist_A` | 0.00 | 0.00 | -1.31 |
| `Whip_Corner_Resist_B` | 0.75 | -2.17 | 3.14 |
| `Whip_Corner_Slump_A` | 0.00 | 0.00 | -1.31 |
| `Whip_Corner_Slump_B` | 0.75 | -2.21 | 3.14 |
| `Whip_Corner_Two_Hand_A` | 0.00 | 0.00 | -1.31 |
| `Whip_Corner_Two_Hand_B` | 0.75 | -2.17 | 3.14 |
| `Whip_Ropes_A` | 0.00 | 0.00 | -1.31 |
| `Whip_Ropes_B` | 0.75 | -3.02 | 0.00 |
| `Whip_Ropes_Fast_A` | 0.00 | 0.00 | -1.31 |
| `Whip_Ropes_Fast_B` | 0.75 | -3.03 | 0.00 |
| `Whip_Ropes_Left_A` | 0.00 | 0.00 | 1.31 |
| `Whip_Ropes_Left_B` | -0.75 | -3.02 | 0.00 |
| `Whip_Ropes_Resist_A` | 0.00 | 0.00 | -1.31 |
| `Whip_Ropes_Resist_B` | 0.75 | -2.98 | 0.00 |
| `Whip_Ropes_Stumble_A` | 0.00 | 0.00 | -1.31 |
| `Whip_Ropes_Stumble_B` | 0.75 | -2.95 | 0.00 |
| `Whip_Ropes_Tired_A` | 0.00 | 0.00 | -1.31 |
| `Whip_Ropes_Tired_B` | 0.75 | -3.00 | 0.00 |
| `Whip_Ropes_Two_Hand_A` | 0.00 | 0.00 | -1.31 |
| `Whip_Ropes_Two_Hand_B` | 0.75 | -3.02 | 0.00 |
| `Whip_Short_Arm_A` | 0.00 | 0.00 | -1.31 |
| `Whip_Short_Arm_B` | 0.75 | 0.10 | 0.00 |

## Ще не готове

- **Рагдол після фінішера.** За брифом падіння після фінішера робить рагдол рушія, кліпа не буде. На сторінці — Bump_Back + Lying_Back_Loop як заміна.
- **Фінішери за ґіміком.** Є лише спільні удари (Center_Lariat_Heavy тощо). Підписні фінішери не зроблені сесією анімацій.
- **Больовий із реальною підсадкою.** Є Chain_Headlock* і власні пози захвату. Парних больових із здачею немає.
- **Кидки й класика (суплекс, пауербомба, DDT, армдраг).** У плані сесії анімацій (ітерація 2, черга 3), кліпів ще немає. Є лише Bodyslam.
- **Робота в куті (удари, удар із розбігу, підсадка).** У плані сесії анімацій (черга 2). Зараз є Whip_Corner_*, Move_Corner_* і чорновий Corner_Strike.
- **Стрибки від канатів і другого каната.** У плані сесії анімацій (черга 4). Зараз — чорнові Top_Dive_Crossbody і Top_Dive_Elbow.
- **Парний удар стільцем / палицею / кейсом по суперникові.** Є одинокі chair_swing, kendo_swing, case_swing; реакція — звичайний бамп.

## Стопи й сходинки

Foot IK у Unity: Humanoid + `Foot IK` у стані; для сходинок і нерівностей — `OnAnimatorIK` із променем униз під кожну стопу, таз опускається на різницю. На сторінці є той самий двокістковий розв'язок.

Ковзання в опорі прибране в самих кліпах руху (`clean_feet` у `clips_lib.py`): стопа нерухома у світі, а швидкість кліпа на місці дорівнює швидкості стопи в опорі. Рухайте персонажа рівно зі `speed` із цього графа, інакше ковзання повернеться.


## Ринг і місця кліпів

Сторінка показує справжній ринг гри: об'єкти `ring_*` із `Assets/Art/Ring/venue_<арена>.fbx`, канати — `RopeSim` за якорями `anchor_rope_*` (`tools/ring_preview/ropes.js` — той самий розрахунок, що `RingRopes.cs`). Розміри беруться з якорів, не з констант.

| Арена | Футів | Висота мату, м | Стійка від центру, м | Лінія каната від центру, м | Край платформи, м |
|---|---|---|---|---|---|
| `arena` | 20 | 1,20 | 2,828 | 2,748 | 3,048 |
| `hall` | 18 | 1,10 | 2,523 | 2,443 | 2,743 |
| `gym` (зараз у грі) | 16 | 1,00 | 2,218 | 2,138 | 2,438 |

Канати скрізь на 0,48 / 0,86 / 1,24 м над матом. Сітка 1,5 м на ринг 16 футів уміщає лише 3 клітини, на 20 футів — 4 (6 м), і крайні клітини заходять за канати: бійцівський квадрат 5,5 м.

Де ставити кліпи (за ними задача 3 будує Animator; hs — зріст постаті відносно скелета, бо Humanoid масштабує корінь):

| Кліп | Початок | Що доробити в рушії |
|---|---|---|
| `Rope_*`, `Move_Rope_Rebound` | лінія каната кліпу — на лінії каната рингу: 1,83 hs від старту кліпу (генератор: ROPE_D + 0,12); для `Move_Rope_Rebound` — найдальша точка кореня + 0,10 hs. Таз у найглибшій точці — від 0 до 0,28 hs перед канатом (за варіантом), спина й кисті — на канаті | канат прогинає фізика; входити в кліп посеред бігу — лише до розвороту спиною (поворот кореня менше 7°) |
| `Move_Corner_*`, `Whip_Corner_*_B` | таз за 0,42 hs від стійки по діагоналі | IK кистей на верхній канат: кліп тримає їх на висоті 1,24 hs |
| `Corner_Climb_Top`, `Top_Rope_Perch_Loop`, `Top_Dive_*` | таз за 0,76 hs від стійки | зсув постаті по висоті: 1,31 − 1,145 hs (чоловічі кліпи), 1,31 − 1,034 hs (жіночі) |
| `Entrance_Pose_Turnbuckle_*` | за 0,9 hs від перетину канатів кута, обличчям до стійки | зсув по висоті 0,86 (1 − hs) за першу третину кліпу |
| `Entrance_Through_Ropes` | на 0,18 hs зовні від лінії каната | |
| `Entrance_Vault_Over_Top` | на 0,32 hs зовні, але не далі 0,06 м від краю платформи | фартух лише 0,30 м завширшки, кліп задуманий на 0,50 м |
| `Entrance_Roll_In` | на підлозі, 0,36 м від краю платформи | постать із підлоги на мат: висота від −мат до −hs |
| `Entrance_Climb_Steps` | якір `anchor_steps_*` на підлозі, обличчям до центру | те саме; сходи під висоту мату: `ring_steps_100/110/120` |

## Ланцюг бою: постановка й стики

Ланцюг бою сторінка розраховує наперед (`tools/ring_preview/director.js`): кожен борець — послідовність відрізків «кліп · з якої миті кліпу · коли · від якої бази · з якими поправками», а програвання кінематичне — борець у кожну мить там, де його поставив план. Правила, які Animator задачі 3 має повторити:

- прийоми й пари — біля центру; пару ставить геометрія в мить контакту (подія `contact`, без неї — мить найближчого проходу), відстань — за середнім зростом пари;
- `Center_*`: жертва стоїть у центрі обличчям до бігуна, нападник вбігає з виходу з канатів; перші ~0,3 с кліпу B стоїть, тож B, що трохи спізнюється, може почати кліп пізніше на ту саму різницю (синхронно за кліповим часом);
- `Whip_Ropes*_B` закінчується бігом: кинутий продовжує в `Rope_*` з тієї ж швидкості й кроку (стик ≈ 2,5 м/с), лінія каната кліпу лягає на канат; із каната — у `Center_*_A` з виходу з канатів, після ухилу — до протилежних канатів без гальмування;
- стики — у мить, коли поза стоп, кистей, голови й висота таза разом зі швидкостями найближчі (зіставлення по кадрах); кросфейд змішує і позу, і швидкість кореня;
- хто переходить на інше місце — переходить кроками: `Move_Turn_*` (до 1,8× швидше, якщо часу мало), `Move_Walk_*`, `U_Walk_Loop`; лише залишок у кілька сантиметрів — поправкою, розподіленою на рухомий відрізок;
- кидок у канат має два боки, `Whip_Ropes` (повз праве плече) і `Whip_Ropes_Left`: бік обирається так, щоб жертві до ухилу було найближче; другий відскок — звичайний, а якщо жертва не встигає розвернутись — повільніший біля канатів (`Rope_Roar`, `Rope_Heavy`, `Rope_Hesitate`);
- після ухилу жертва ліворуч від бігуна, тож удар перед удержанням — з того боку й на спину: `Center_ShoulderBlock`, `Center_ShoulderBlock_Heavy`;
- на малому рингу для високої пари (Молот на 16 і 18 футах) — скорочений ланцюг: кидок у канат → відскок → одразу удар.

## Рух по рингу: цикли й стани руху

- біг — від каната до каната вздовж осі через центр (z чи x); відскок розвертає не рівно на 180°, тож курс вирівнюється дугою самого бігу (на вставці між канатами й на вибігу після відштовхування; шлях гнеться разом із тілом, без ковзання вбік), а бічний зсув знімається прицілом на лінію через центр (до 0,22 рад); лінію каната кліпу ставимо на площину каната вздовж осі рингу, навіть якщо бігун заходить під кутом; з довільної точки — дугою радіуса 1 м на ту вісь, де після дуги попереду найбільше місця (не менше 1,3 м) і найменший зсув від центру, а дуга не підходить до канатів ближче 0,45 м;
- розгін (`Run_Start`) — з осі через центр, відступивши рівно стільки, щоб після нього до каната лишилося місце на вхід у відскок до розвороту (до 0,2 м добирає поправка); вісь — та, куди дійти найшвидше (до 1,15 м — кроками в стійці, без розворотів); якщо розгін не вміщається (високий борець на малому рингу), він обривається там, де це місце ще є;
- біг спиною (`Run_Backpedal_Loop`) канатами не бігає: кліпи відскоку починаються бігом обличчям до каната; він, як хода, — колом;
- хода — колом навколо центру радіуса «лінія каната − 0,95 м» (від 1,1 до 1,8 м); з будь-якої точки — керованими дугами (радіус не менше 0,8 м) на це коло, у той бік, куди борець уже йде;
- кружляння в стійці — навколо центру рингу (центр кола — з кривої самого кліпу);
- вихід зі стану руху — у найближчу мить, коли борець біжить чи йде сам, а не лежить спиною на канаті (відскок уже відштовхнув, +0,12 с); кліп, якому треба місце попереду (зупинка з бігу), — ще й коли попереду є його довжина + 0,45 м.


## Збірка й перевірка

- Сторінка: `python tools/ring_preview/run.py` (без інтернету).
- Усе з нуля, ≈ 8 хв: `sh tools/blender/ring_cast/build_all.sh <робоча тека> [персонажі]`; `SKIP_CLIPS=1` не чіпає кліпи. Спершу кеш кліпів: `extract_clips.py`.
- Вихідні `.blend` у `ArtSource/Ring/Cast/` (поза git, 1,5 ГБ): їх дає та сама збірка.
- Кліпи числами: `python tools/blender/ring_cast/clips_qa.py <кліпи>` (шви, стрибки, стопи, ковзання); пари: `pair_check.py`; одяг: `cast_cloth_check.py`.
- Імпорт у Unity: `powershell -File tools/blender/ring_cast/unity_check/run_check.ps1 -Work <тимчасова тека>` (окремий проєкт, без вікна): Humanoid, Avatar, програвання кліпів на кожній моделі.
- Кожна модель має власний Avatar з однаковою Humanoid-розміткою, а не один спільний файл: пропорції різні. Кліпи з `RING_Cast_M.fbx` і `RING_Cast_F.fbx` лягають на всі моделі через Humanoid.