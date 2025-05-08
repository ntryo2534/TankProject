using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;

namespace Tanks.Complete
{
    //TankShootingからInputUserを取得するため、このコンポーネントを先に設定するようにしてください。
    [DefaultExecutionOrder(-10)]
    public class TankMovement : MonoBehaviour
    {
        [Tooltip("The player number. Without a tank selection menu, Player 1 is left keyboard control, Player 2 is right keyboard")]
        public int m_PlayerNumber = 1;              // プレイヤー番号　保持
        [Tooltip("The speed in unity unit/second the tank move at")]
        public float m_Speed = 12f;                 // 戦車の走行スピード
        [Tooltip("The speed in deg/s that tank will rotate at")]
        public float m_TurnSpeed = 180f;            // 戦車の旋回スピード
        [Tooltip("Trueの時、旋回し進むのではなく、入力方向に進むように変更")]
        public bool m_IsDirectControl;
        public AudioSource m_MovementAudio;         // 音声再生AudioSource
        public AudioClip m_EngineIdling;            // 待機時の音声
        public AudioClip m_EngineDriving;           // 走行時の音声
		public float m_PitchRange = 0.2f;           // 音声にランダム制を持たせるピッチの幅
        [Tooltip("TrueでCPUに切り替える")]
        public bool m_IsComputerControlled = false; // CPU操作かどうか
        [HideInInspector]
        public TankInputUser m_InputUser;            // 戦車操作用　InputAction
        
        public Rigidbody Rigidbody => m_Rigidbody;
        
        public int ControlIndex { get; set; } = -1; //操作方法　1:左側キーボード操作　2:右側キーボード操作　-1:コントローラーなし（無くすかも）
        
        private string m_MovementAxisName;          // 進む時の入力軸の名前
        private string m_TurnAxisName;              // 旋回時の入力軸の名前
        private Rigidbody m_Rigidbody;              // 戦車が動くときに使うRigidbody
        private float m_MovementInputValue;         // 進む時の入力値
        private float m_TurnInputValue;             // 旋回時の入力値
        private float m_OriginalPitch;              // 再生した時のオーディオソース元々のピッチ保持
        private ParticleSystem[] m_particleSystems; // 戦車動く際に使うパーティクルシステム保持
        
        private InputAction m_MoveAction;             // 移動時に使うInput Action
        private InputAction m_TurnAction;             // 旋回時に使うInput Action

        private Vector3 m_RequestedDirection;       // m_IsDirectControlがTrueの時の、入力方向 保持変数

        private void Awake ()
        {
            m_Rigidbody = GetComponent<Rigidbody> ();
            
            m_InputUser = GetComponent<TankInputUser>();
            if (m_InputUser == null)
                m_InputUser = gameObject.AddComponent<TankInputUser>();
        }


        private void OnEnable ()
        {
            // 戦車が動けるようにするため
            m_Rigidbody.isKinematic = false;

            // 入力値のリセット
            m_MovementInputValue = 0f;
            m_TurnInputValue = 0f;

            // このGameObjectの子供になっている全てのパーティクルを保持する
            //（リスポーン時等で、移動のトレイルパーティクルを出さないようにするため…）
            m_particleSystems = GetComponentsInChildren<ParticleSystem>();
            for (int i = 0; i < m_particleSystems.Length; ++i)
            {
                m_particleSystems[i].Play();
            }
        }


        private void OnDisable ()
        {
            // 戦車が動けないように
            m_Rigidbody.isKinematic = true;

            // 戦車のリスポーン時の移動中にトレイルパーティクルがでないよう再生停止
            for(int i = 0; i < m_particleSystems.Length; ++i)
            {
                m_particleSystems[i].Stop();
            }
        }


        private void Start ()
        {
            // CPU操作の場合
            if (m_IsComputerControlled)
            {
                //　AIコンポーネントを所持しているか？
                var ai = GetComponent<TankAI>();
                if (ai == null)
                {
                    // ない場合はCPU操作に切り替えるため、TankAI CommpornentをAddする
                    gameObject.AddComponent<TankAI>();
                }
            }

            // CPU操作ではなく、ControlIndexが-1に設定されていた場合、
            // Inspectorに設定されている m_PlayerNumber の値に変更する
            if (ControlIndex == -1 && !m_IsComputerControlled)
            {
                ControlIndex = m_PlayerNumber;
            }
            
            ///TODO:オンライン操作周りの実装をするため、変わるかも
            var mobileControl = FindAnyObjectByType<MobileUIControl>();
            
            // スマホ操作用仮想ゲームパッドコンポーネントが存在している時
            if (mobileControl != null && ControlIndex == 1)
            {
                m_InputUser.SetNewInputUser(InputUser.PerformPairingWithDevice(mobileControl.Device));
                m_InputUser.ActivateScheme("Gamepad");
            }
            else
            {
                // 上記のコンポーネントがない時、ControlIndexが１の時はWASD操作、2の時は矢印キー操作
                m_InputUser.ActivateScheme(ControlIndex == 1 ? "KeyboardLeft" : "KeyboardRight");
            }

            // The axes names are based on player number.
            m_MovementAxisName = "Vertical";
            m_TurnAxisName = "Horizontal";

            //TankInputUser から取得した入力内容をcontrol schemeに設定する
            m_MoveAction = m_InputUser.ActionAsset.FindAction(m_MovementAxisName);
            m_TurnAction = m_InputUser.ActionAsset.FindAction(m_TurnAxisName);
            
            // 入力を有効化しておく
            m_MoveAction.Enable();
            m_TurnAction.Enable();
            
            // 元のピッチを保持しておく
            m_OriginalPitch = m_MovementAudio.pitch;
        }


        private void Update ()
        {
            //　プレイヤー操作処理のみを動作させる
            if (!m_IsComputerControlled)
            {
                //移動、旋回の入力値を取得し続ける
                m_MovementInputValue = m_MoveAction.ReadValue<float>();
                m_TurnInputValue = m_TurnAction.ReadValue<float>();
            }
            
            EngineAudio ();
        }


        /// <summary>
        /// 戦車の音声
        /// </summary>
        private void EngineAudio ()
        {
            // 止まっているとき（移動、旋回の入力がない時）
            if (Mathf.Abs (m_MovementInputValue) < 0.1f && Mathf.Abs (m_TurnInputValue) < 0.1f)
            {
                // 現在、移動、旋回時の音声を再生中の場合
                if (m_MovementAudio.clip == m_EngineDriving)
                {
                    // 停止中の音声に切り替え、再生する
                    m_MovementAudio.clip = m_EngineIdling;
                    m_MovementAudio.pitch = Random.Range (m_OriginalPitch - m_PitchRange, m_OriginalPitch + m_PitchRange);
                    m_MovementAudio.Play ();
                }
            }
            else
            {
                // 戦車が移動中で停止中の音声が再生されている場合
                if (m_MovementAudio.clip == m_EngineIdling)
                {
                    // 移動、旋回時の音声に切り替え、再生する
                    m_MovementAudio.clip = m_EngineDriving;
                    m_MovementAudio.pitch = Random.Range(m_OriginalPitch - m_PitchRange, m_OriginalPitch + m_PitchRange);
                    m_MovementAudio.Play();
                }
            }
        }


        private void FixedUpdate ()
        {
            // GamePad入力か、m_IsDirectControlがTrueの場合、戦車の移動方法を入力方向に移動するように変更する
            if (m_InputUser.InputUser.controlScheme.Value.name == "Gamepad" ||  m_IsDirectControl)
            {
                var camForward = Camera.main.transform.forward;
                camForward.y = 0;
                camForward.Normalize();
                var camRight = Vector3.Cross(Vector3.up, camForward);
                
                //カメラの向いている向きからの入力方向に向かって動くように
                m_RequestedDirection = (camForward * m_MovementInputValue + camRight * m_TurnInputValue);
            }

            // FixedUpdate にてRigidBodyの位置、方向調整を行います
            Move();
            Turn ();
        }


        private void Move ()
        {
            float speedInput = 0.0f;

            // GamePad入力か、m_IsDirectControlがTrueの場合
            if (m_InputUser.InputUser.controlScheme.Value.name == "Gamepad" || m_IsDirectControl)
            {
                speedInput = m_RequestedDirection.magnitude;
                //入力方向が現在の戦車の向きに対し、90度未満の場合は最高速度、90～180度の間で減速します。
                speedInput *= 1.0f - Mathf.Clamp01((Vector3.Angle(m_RequestedDirection, transform.forward) - 90) / 90.0f);
            }
            else
            {
                // 通常操作（旋回ありの場合は、前入力方向）
                speedInput = m_MovementInputValue;
            }
            
            // 戦車の向いている方向に移動する
            Vector3 movement = transform.forward * speedInput * m_Speed * Time.deltaTime;

            // RigidBodyに移動値を適応する
            m_Rigidbody.MovePosition(m_Rigidbody.position + movement);
        }


        private void Turn ()
        {
            Quaternion turnRotation;
            // GamePad入力か、m_IsDirectControlがTrueの場合
            if (m_InputUser.InputUser.controlScheme.Value.name == "Gamepad" || m_IsDirectControl)
            {
                // 入力方向に向くための計算
                float angleTowardTarget = Vector3.SignedAngle(m_RequestedDirection, transform.forward, transform.up);
                var rotatingAngle = Mathf.Sign(angleTowardTarget) * Mathf.Min(Mathf.Abs(angleTowardTarget), m_TurnSpeed * Time.deltaTime);
                turnRotation = Quaternion.AngleAxis(-rotatingAngle, Vector3.up);
            }
            else
            {
                float turn = m_TurnInputValue * m_TurnSpeed * Time.deltaTime;

                // Y軸の回転処理
                turnRotation = Quaternion.Euler (0f, turn, 0f);
            }

            // RigidBodyに回転を適応する
            m_Rigidbody.MoveRotation (m_Rigidbody.rotation * turnRotation);
        }
    }
}