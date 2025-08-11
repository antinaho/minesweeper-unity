using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Audio;
using Random = System.Random;

public class Game : MonoBehaviour {
    
    [Header("Scene References")]
    public Camera MainCamera;
    public TMP_Text RemainingBombsTXT;
    public TMP_Text SolveTimerTXT;
    public GameObject WinScreen;
    public GameObject LossScreen;
    
    [Header("Prefabs")]
    public SpriteRenderer TilePrefab;
    public SpriteRenderer TileValuePrefab;
    public Transform RedFlagPrefab;
    public Transform BombPrefab;
    public AudioSource AudioSourcePrefab;
    
    [Header("Sounds")]
    public AudioMixer Mixer;
    public AudioClip AudioButtonClick;
    public AudioClip AudioOpenTile;
    public AudioClip AudioFlagPlace;
    public AudioClip AudioFlagRemove;
    public AudioClip AudioBombExplode;

    [Header("Sprites")] 
    public Sprite EvenBG;
    public Sprite UnevenBG;
    public Color BaseColor;
    public Color HighlightColor;
    public Sprite EvenSprite;
    public Sprite UnevenSprite;
    [Space]
    public Sprite One;
    public Sprite Two;
    public Sprite Three;
    public Sprite Four;
    public Sprite Five;
    public Sprite Six;
    public Sprite Seven;
    public Sprite Eight;
    
    [Header("Colors")]
    public Color ColorOne;
    public Color ColorTwo;
    public Color ColorThree;
    public Color ColorFour;
    public Color ColorFive;
    public Color ColorSix;
    public Color ColorSeven;
    public Color ColorEight;
    
    // *******************************************************************
    
    private const int LeftClick = 0;
    private const int RightClick = 1;
    
    private GameState _state;
    private GameDifficulty _chosenDifficulty;
    private bool _hasStarted;
    private float _solveTime;
    
    private ObjectPool<Transform> _flagPool;
    private ObjectPool<Transform> _bombPool;
    private ObjectPool<AudioSource> _audioPool;
    private ObjectPool<SpriteRenderer> _tileValues;
    
    private bool _volumeMuted;
    
    private Grid _grid;
    
    private Tile _currentTileSelection;
    
    private float _lostTimer;
    private float _lostTimerMax;
    
    private readonly (int dx, int dy)[] _neighbourOffsets8Way = new[] {
        (-1, 1), (0, 1), (1, 1),
        (-1, 0),         (1, 0),
        (-1, -1), (0, -1), (1, -1),
    };

    private readonly DifficultyConfig _easy = new DifficultyConfig(
        size:            (10, 8),
        bombs:           10,
        cameraPos:       new Vector3(4f, 3.5f, -10f),
        cameraOrthoSize: 4.5f
    );
    
    private readonly DifficultyConfig _normal = new DifficultyConfig(
        size:           (18, 14),
        bombs:          40,
        cameraPos:      new Vector3(8.5f, 6.5f, -10f),
        cameraOrthoSize:7.5f
    );
    
    private readonly DifficultyConfig _hard = new DifficultyConfig(
        size:           (24, 20),
        bombs:          99,
        cameraPos:      new Vector3(12f, 9.5f, -10f),
        cameraOrthoSize:10.5f
    );
    
    // UNITY EVENTS
    
    private void Awake() {
        
        WinScreen.SetActive(false);
        LossScreen.SetActive(false);
        
        _grid = new Grid(
            maxDimensions: _hard.BoardSize,
            maxBombs: _hard.Bombs
        );

        _grid.TileUnityObjects = new SpriteRenderer[_grid.MaxDimensions.width * _grid.MaxDimensions.height];
        Transform tileParent = new GameObject("Tiles").transform;
        for (int i = 0; i < _grid.TileUnityObjects.Length; i++) {
            int x = i % _grid.MaxDimensions.width;
            int y = i / _grid.MaxDimensions.width;
            Vector3 position = new Vector3(x, y, 0f);
            SpriteRenderer tile = Instantiate(TilePrefab, position, Quaternion.identity, tileParent);
            _grid.TileUnityObjects[i] = tile;
        }

        _tileValues = new ObjectPool<SpriteRenderer>(_grid.MaxDimensions.width * _grid.MaxDimensions.height);
        ObjectPoolFill(_tileValues, TileValuePrefab);
        
        _flagPool = new ObjectPool<Transform>(128);
        ObjectPoolFill(_flagPool, RedFlagPrefab);
        
        
        _bombPool = new ObjectPool<Transform>(_grid.MaxBombs);
        ObjectPoolFill(_bombPool, BombPrefab);
        
        _audioPool = new ObjectPool<AudioSource>(256);
        ObjectPoolFill(_audioPool, AudioSourcePrefab);
        
        _state = GameState.GameActive;

        _chosenDifficulty = GameDifficulty.Easy;

        GameRestart(_chosenDifficulty);
    }
    
    private void Start() {
        Mixer.SetFloat("MasterVolume", -20f);
    }
    
    private void Update() {

        switch (_state) {
            case GameState.GameWon:
                
                if (Input.GetKeyDown(KeyCode.R)) {
                    GameRestart(_chosenDifficulty);
                    _state = GameState.GameActive;
                    return;
                }
                
                break;
            
            case GameState.GameLost:
                _lostTimer += Time.deltaTime;

                if (_lostTimer > _lostTimerMax && _grid.BombTiles.Count > 0) {
                    _lostTimerMax *= 0.87f;
                    _lostTimerMax = Mathf.Max(_lostTimerMax, 0.2f);
                    _lostTimer = 0f;
                    Tile t = _grid.BombTiles[^1];
                    TileRevealBomb(t);
                    _grid.BombTiles.Remove(t);

                    AudioPlay(AudioBombExplode);
                }
                
                if (Input.GetKeyDown(KeyCode.R)) {
                    GameRestart(_chosenDifficulty);
                    _state = GameState.GameActive;
                    return;
                }
                
                break;
            
            case GameState.GameActive:
                
                if (_hasStarted) {
                    _solveTime += Time.deltaTime;
                    SolveTimerTXT.SetText(Mathf.RoundToInt(_solveTime).ToString());    
                }

                Vector3 mousePos = Input.mousePosition;
                mousePos.z = -MainCamera.transform.position.z;
                Vector2 worldPoint = MainCamera.ScreenToWorldPoint(mousePos);
                Collider2D hitCollider = Physics2D.OverlapPoint(worldPoint);
                bool detectHit = hitCollider != null && hitCollider.GetComponent<BoxCollider2D>() != null;

                if (!detectHit) {
                    if (_currentTileSelection != null) {
                        DeHighlightTile(_currentTileSelection);
                        _currentTileSelection = null;
                    }

                    break;
                }
                
                (int x, int y) tilePos = WorldSpaceToGrid(worldPoint);
                Tile tile = TileFromPosition(tilePos);

                //Highlight selection
                if (_currentTileSelection != null && tile != _currentTileSelection) {
                    DeHighlightTile(_currentTileSelection);

                    if (tile.Flags.HasFlag(TileFlags.IsHidden)) {
                        _currentTileSelection = tile;
                        HighlightTile(tile);    
                    }
                    else {
                        _currentTileSelection = null;
                    }
                } else if (_currentTileSelection == null) {
                    _currentTileSelection = tile;
                    HighlightTile(_currentTileSelection);
                }

                //Click logic
                if (Input.GetMouseButtonDown(LeftClick)) {
                    //Start solve timer
                    if (!_hasStarted) _hasStarted = true;
                    
                    if (tile.Flags.HasFlag(TileFlags.IsFlagged)) {
                        return;
                    }
                    
                    if (TileIsHidden(tile)) {
                        //Bomb
                        if (tile.Flags.HasFlag(TileFlags.IsBomb)) {
                            _currentTileSelection = null;
                            
                            AudioPlay(AudioBombExplode);
                            
                            
                            TileRevealBomb(tile);
                            _lostTimerMax = 0.5f;
                            _lostTimer = 0f;
                            
                            ReturnAllFlags();

                            _state = GameState.GameLost;
                            LossScreen.SetActive(true);
                            
                            return;
                        }
                        //Flood reveal 0s
                        else if (tile.Value == 0) {
                            FloodRevealOtherZeroValues(tile);
                            
                            AudioPlay(AudioOpenTile);
                            
                        }
                        //Normal
                        else {
                            _grid.RemainingSafeTiles -= 1;
                            TileNumberReveal(tile);
                            
                            AudioPlay(AudioOpenTile);
                            
                        }

                        //Win?
                        if (_grid.RemainingSafeTiles == 0) {
                            
                            WinScreen.SetActive(true);
                            
                            if (_currentTileSelection != null) {
                                DeHighlightTile(_currentTileSelection);
                                _currentTileSelection = null;
                            }
                            
                            ReturnAllFlags();
                            
                            _state = GameState.GameWon;
                            return;
                        }
                    }
                    
                }
                else if (Input.GetMouseButtonDown(RightClick)) {
                    //Toggle flag on/off
                    if (TileIsHidden(tile) && !tile.Flags.HasFlag(TileFlags.IsFlagged)) {
                        
                        AudioPlay(AudioFlagPlace);
                        
                        
                        tile.Flags |= TileFlags.IsFlagged;
                        TilePlaceFlag(tile);
                        _grid.RemainingBombs -= 1;
                        RemainingBombsTXT.SetText(_grid.RemainingBombs.ToString());
                    }
                    else if (tile.Flags.HasFlag(TileFlags.IsFlagged)) {
                        
                        AudioPlay(AudioFlagRemove);
                        
                        tile.Flags &= ~TileFlags.IsFlagged;
                        TileRemoveFlag(tile);
                        _grid.RemainingBombs += 1;
                        RemainingBombsTXT.SetText(_grid.RemainingBombs.ToString());
                    }
                }
                
                break;
        }
    }
    
    // UI & Buttons
    
    public void MuteSoundButton() {
        
        AudioPlay(AudioButtonClick);
        
        _volumeMuted = !_volumeMuted;
        float db = _volumeMuted ? -80f : -20f;
        Mixer.SetFloat("MasterVolume", db);
    }
    
    public void RestartButton() {
        
        AudioPlay(AudioButtonClick);
        
        GameRestart(_chosenDifficulty);
        _state = GameState.GameActive;
    }
    
    public void ChangeDifficultyButton() {
        
        AudioPlay(AudioButtonClick);
        
        int current = (int)_chosenDifficulty;
        _chosenDifficulty = (GameDifficulty)Enum.GetValues(typeof(GameDifficulty)).GetValue((current + 1) % 3);
        
        GameRestart(_chosenDifficulty);
        _state = GameState.GameActive;
    }
    
    // Audio
    
    private void AudioPlay(AudioClip clip) {
        if (ObjectPoolTryGet(_audioPool, out AudioSource source)) {
            source.clip = clip;
            source.Play();
            StartCoroutine(WaitTillSoundEnd(source));
        }
    }
    
    private IEnumerator WaitTillSoundEnd(AudioSource source) {
        yield return new WaitUntil(() => !source.isPlaying);
        source.Stop();
        ObjectPoolReturn(_audioPool, source);
    }
    
    // Object pool
    
    private void ObjectPoolFill<T>(ObjectPool<T> pool, T prefab, Transform parent = null) where T : Component {
        parent ??= new GameObject($"({prefab.name}) Pool").transform;
        
        for (int i = 0; i < pool.Capacity; i++)
        {
            CreateObject();
        }
        
        void CreateObject()
        {
            T obj = UnityEngine.Object.Instantiate(prefab, parent);
            obj.gameObject.SetActive(false);
            pool.AvailableObjects.Enqueue(obj);
        }
    }
    
    private bool ObjectPoolTryGet<T>(ObjectPool<T> pool, out T obj) where T : Component
    {
        // If there are available objects in the pool
        if (pool.AvailableObjects.Count > 0)
        {
            obj = pool.AvailableObjects.Dequeue();
        }
        else
        {
            // Pool is exhausted, get the oldest active object and recycle it
            if (pool.UsedObjects.Count > 0)
            {
                obj = pool.UsedObjects[0];
                pool.UsedObjects.RemoveAt(0);
            }
            else {
                obj = null;
                return false;
            }
        }
        
        obj.gameObject.SetActive(true);
        pool.UsedObjects.Add(obj);
        return true;
    }

    private void ObjectPoolReturnAll<T>(ObjectPool<T> pool) where T : Component
    {
        while (pool.UsedObjects.Count > 0)
        {
            ObjectPoolReturn(pool, pool.UsedObjects[0]);
        }
    }
    
    private void ObjectPoolReturn<T>(ObjectPool<T> pool, T obj) where T : Component
    {
        if (obj == null) return;
        
        obj.gameObject.SetActive(false);
        pool.UsedObjects.Remove(obj);
        pool.AvailableObjects.Enqueue(obj);
    }
    
    private T ObjectPoolGet<T>(ObjectPool<T> pool) where T : Component {
        T obj;
        // If there are available objects in the pool
        if (pool.AvailableObjects.Count > 0)
        {
            obj = pool.AvailableObjects.Dequeue();
        }
        else
        {
            // Pool is exhausted, get the oldest active object and recycle it
            if (pool.UsedObjects.Count > 0)
            {
                obj = pool.UsedObjects[0];
                pool.UsedObjects.RemoveAt(0);
            }
            else {
                return null;
            }
        }
        
        obj.gameObject.SetActive(true);
        pool.UsedObjects.Add(obj);
        return obj;
    }
    
    // GAME LOGIC
    
    private void ReturnAllFlags() {
        for (int i = _flagPool.UsedObjects.Count - 1; i >= 0; i--) {
            Transform t = _flagPool.UsedObjects[i];
            ReturnFlag(t);
        }   
    }
    
    private void ReturnFlag(Transform t) {
        float jumpPower = 1.4f;
        float jumpDuration = 0.8f;
        float randomX = UnityEngine.Random.Range(-0.3f, 0.3f);
        Vector3 endPosition = new Vector3(t.position.x + randomX,
            t.position.y - 0.8f, 0f);
        float randomAngle = UnityEngine.Random.Range(-150f, 150f);
            
        Sequence mySequence = DOTween.Sequence();
        mySequence
            .Insert(0, t.DOJump(endPosition, jumpPower, 1, jumpDuration, false))
            .Insert(0, t.DOScale(0.3f, 0.8f))
            .Insert(0, t.DORotate(new Vector3(0f, 0f, randomAngle), 0.8f))
            .OnComplete(() => ObjectPoolReturn(_flagPool, t));
    }
    
    private void GameRestart(GameDifficulty difficulty) {
        _solveTime = 0f;
        _hasStarted = false;
        SolveTimerTXT.SetText("0");

        _grid.Dimensions = IndexToConfig((int)difficulty).BoardSize; 
        int numberOfBombs = IndexToConfig((int)difficulty).Bombs; 
        
        _grid.RemainingSafeTiles = _grid.Width * _grid.Height - numberOfBombs;
        _grid.RemainingBombs = numberOfBombs;
        RemainingBombsTXT.SetText(_grid.RemainingBombs.ToString());

        Vector3 cameraPosition = IndexToConfig((int)difficulty).CameraPosition; 
        float cameraOrthoSize = IndexToConfig((int)difficulty).CameraOrthoSize; 
        MainCamera.transform.position = cameraPosition;
        MainCamera.orthographicSize = cameraOrthoSize;
        
        DifficultyConfig IndexToConfig(int index) => index switch {
            0 => _easy,
            1 => _normal,
            2 => _hard,
            _ => throw new ArgumentOutOfRangeException(nameof(index), index, null)
        }; 
        
        _grid.Tiles = new Tile[_grid.Width * _grid.Height];

        ObjectPoolReturnAll(_flagPool);
        
        for (int y = 0; y < _grid.MaxDimensions.height; y++) {
            for (int x = 0; x < _grid.MaxDimensions.width; x++) {
                
                int gridIndex = y * _grid.MaxDimensions.width + x;
                if (y >= _grid.Height || x >= _grid.Width) {
                    _grid.TileUnityObjects[gridIndex].gameObject.SetActive(false);
                    continue;
                }
                
                int tileIndex = y * _grid.Width + x;
                _grid.Tiles[tileIndex] = new Tile(
                    x: x,
                    y: y,
                    flags: TileFlags.IsHidden,
                    renderer: _grid.TileUnityObjects[gridIndex],
                    sprite: (x + y) % 2 == 0 ? EvenSprite : UnevenSprite,
                    collider: _grid.TileUnityObjects[gridIndex].GetComponent<BoxCollider2D>()
                );
                _grid.Tiles[tileIndex].Collider.enabled = true;
                _grid.Tiles[tileIndex].UnityTileObject.gameObject.SetActive(true);
                _grid.Tiles[tileIndex].UnityTileObject.sprite = _grid.Tiles[tileIndex].Sprite;
                _grid.Tiles[tileIndex].UnityTileObject.color = BaseColor;
            }
        }

        ObjectPoolReturnAll(_bombPool);

        _grid.BombTiles = new List<Tile>();
        Random random = new Random(); 
        List<int> indices = new List<int>(Enumerable.Range(0, _grid.Width * _grid.Height)); 
        for (int i = 0; i < numberOfBombs; i++) { 
            int remaining = indices.Count - i; 
            int randomIndex = random.Next(0, remaining); 
            int tileIndex = indices[randomIndex];
            _grid.Tiles[tileIndex].Flags |= TileFlags.IsBomb;
            _grid.Tiles[tileIndex].Bomb = ObjectPoolGet(_bombPool);
            _grid.BombTiles.Add(_grid.Tiles[tileIndex]);
            Vector3 pos = new Vector3(_grid.Tiles[tileIndex].X, _grid.Tiles[tileIndex].Y, -0.05f);
            _grid.Tiles[tileIndex].Bomb.position = pos;
            _grid.Tiles[tileIndex].Bomb.gameObject.SetActive(false);
            indices[randomIndex] = indices[remaining - 1]; 
            indices.RemoveAt(remaining - 1);
        }

        ObjectPoolReturnAll(_tileValues);
        
        for (int i = 0; i < _grid.Tiles.Length; i++) {
            Tile tile = _grid.Tiles[i];
            if (tile.Flags.HasFlag(TileFlags.IsBomb))
                continue;
            foreach ((int dx, int dy) in _neighbourOffsets8Way) {
                int nx = tile.X + dx;
                int ny = tile.Y + dy;
        
                if (nx >= 0 && nx < _grid.Width && ny >= 0 && ny < _grid.Height) {
                    Tile neighbour = TileFromPosition((nx, ny));
                    if (neighbour.Flags.HasFlag(TileFlags.IsBomb))
                        tile.Value += 1;
                }
            }

            SpriteRenderer text = ObjectPoolGet(_tileValues);
            text.gameObject.SetActive(false);
            text.transform.position = new Vector3(tile.X, tile.Y, -0.2f);
            text.sprite = ValueSprite(tile.Value);
            text.color = ValueColor(tile.Value);
            tile.Text = text;
            
        }
        
        WinScreen.SetActive(false);
        LossScreen.SetActive(false);
    }
    
    private Sprite ValueSprite(int value) {
        return value switch {
            1 => One,
            2 => Two,
            3 => Three,
            4 => Four,
            5 => Five,
            6 => Six,
            7 => Seven,
            8 => Eight,
            _ => null,
        };
    }

    private Color ValueColor(int value) {
        return value switch {
            1 => ColorOne,
            2 => ColorTwo,
            3 => ColorThree,
            4 => ColorFour,
            5 => ColorFive,
            6 => ColorSix,
            7 => ColorSeven,
            8 => ColorEight,
            _ => Color.white,
        };
    }
    
    private void HighlightTile(Tile tile) {
        tile.UnityTileObject.color = HighlightColor;
    }

    private void DeHighlightTile(Tile tile) {
        tile.UnityTileObject.color = BaseColor;
    }
    
    private enum GameDifficulty {
        Easy,
        Normal,
        Hard,
    }
    
    private void FloodRevealOtherZeroValues(Tile tile) {

        if (tile.Flags.HasFlag(TileFlags.IsFlagged)) {
            TileRemoveFlag(tile);
            _grid.RemainingBombs += 1;
            RemainingBombsTXT.SetText(_grid.RemainingBombs.ToString());
        }
        
        TileReveal(tile);
        _grid.RemainingSafeTiles -= 1;
        foreach ((int dx, int dy) in _neighbourOffsets8Way) {
            int nx = tile.X + dx;
            int ny = tile.Y + dy;

            if (nx >= 0 && nx < _grid.Width && ny >= 0 && ny < _grid.Height) {
                Tile neighbour = TileFromPosition((nx, ny));
                if (!TileIsHidden(neighbour)) continue;
                if (TileIsBomb(neighbour)) continue;

                if (neighbour.Value == 0) {
                    FloodRevealOtherZeroValues(neighbour);
                }
                else {
                    if (neighbour.Flags.HasFlag(TileFlags.IsFlagged)) {
                        TileRemoveFlag(neighbour);
                        _grid.RemainingBombs += 1;
                        RemainingBombsTXT.SetText(_grid.RemainingBombs.ToString());
                    }
                    TileNumberReveal(neighbour);
                    _grid.RemainingSafeTiles -= 1;
                }
            }
        }
    }
    
    private void TileRemoveFlag(Tile tile) {
        Transform t = tile.Flag;
        ReturnFlag(t);
        tile.Flag = null;
    }
    
    private void TilePlaceFlag(Tile tile) {

        if (ObjectPoolTryGet(_flagPool, out Transform t)) {
            t.localScale = Vector3.one;
            t.eulerAngles = Vector3.zero;
            t.position = new Vector3(tile.X, tile.Y, -0.3f);
            
            tile.Flag = t;
        }
    }
    
    private bool TileIsBomb(Tile tile) {
        return tile.Flags.HasFlag(TileFlags.IsBomb);
    }
    
    private Tile TileFromPosition((int x, int y) position) {
        return _grid.Tiles[position.y * _grid.Width + position.x];
    }

    private void TileNumberReveal(Tile tile) {
        tile.Flags &= ~TileFlags.IsHidden;
        tile.Collider.enabled = false;
        tile.Sprite = (tile.X + tile.Y) % 2 == 0 ? EvenBG : UnevenBG;
        tile.UnityTileObject.sprite = tile.Sprite;
        tile.Text.gameObject.SetActive(true);
    }
    
    private void TileReveal(Tile tile) {
        tile.Flags &= ~TileFlags.IsHidden;
        tile.UnityTileObject.color = BaseColor;
        tile.Sprite = (tile.X + tile.Y) % 2 == 0 ? EvenBG : UnevenBG;
        tile.UnityTileObject.sprite = tile.Sprite;
        tile.Text.gameObject.SetActive(false);
        tile.Collider.enabled = false;
    }
    
    private void TileRevealBomb(Tile tile) {
        tile.Flags &= ~TileFlags.IsHidden;
        tile.Collider.enabled = false;
        tile.Bomb.gameObject.SetActive(true);
        Sprite sprite = (tile.X + tile.Y) % 2 == 0 ? EvenSprite : UnevenSprite;
        tile.Sprite = sprite;
        tile.UnityTileObject.sprite = sprite;
    }

    private (int, int) WorldSpaceToGrid(Vector3 position) {
        return (Mathf.RoundToInt(position.x), Mathf.RoundToInt(position.y));
    }

    private bool TileIsHidden(Tile tile) {
        return tile.Flags.HasFlag(TileFlags.IsHidden);
    }
}

public enum GameState {
    GameActive,
    GameLost,
    GameWon,
}

public class Tile {
    public int X;
    public int Y;
    public int Value;
    
    public SpriteRenderer UnityTileObject;
    public BoxCollider2D Collider;
    public Sprite Sprite;
    
    public Transform Bomb;
    public TileFlags Flags;
    public Transform Flag;
    public SpriteRenderer Text;

    public Tile(int x, int y, TileFlags flags, SpriteRenderer renderer, Sprite sprite, BoxCollider2D collider) {
        X = x;
        Y = y;
        Flags = flags;
        UnityTileObject = renderer;
        Sprite = sprite;
        Collider = collider;
    }
}

[Flags]
public enum TileFlags : ulong {
    IsHidden = 1 << 0,
    IsBomb = 1 << 1,
    IsFlagged = 1 << 2,
}

public class Grid {

    public int Width => Dimensions.width;
    public int Height => Dimensions.height;
    public (int width, int height) Dimensions;
    public int RemainingSafeTiles;
    public int RemainingBombs;
    public Tile[] Tiles;
    public List<Tile> BombTiles;
    public SpriteRenderer[] TileUnityObjects;
    
    public readonly (int width, int height) MaxDimensions;
    public readonly int MaxBombs;
    
    public Grid((int, int) maxDimensions, int maxBombs) {
        MaxDimensions = maxDimensions;
        MaxBombs = maxBombs;
    }
}

public class ObjectPool<T> where T : Component
{
    public readonly Queue<T> AvailableObjects;
    public readonly List<T> UsedObjects;
    public readonly int Capacity;
    
    public ObjectPool(int capacity) {
        Capacity = capacity;
        AvailableObjects = new Queue<T>(Capacity);
        UsedObjects = new List<T>(Capacity);
    }
}

public class DifficultyConfig {
    public (int width, int height) BoardSize;
    public int Bombs;
    public Vector3 CameraPosition;
    public float CameraOrthoSize;

    public DifficultyConfig((int, int) size, int bombs, Vector3 cameraPos, float cameraOrthoSize) {
        BoardSize = size;
        Bombs = bombs;
        CameraPosition = cameraPos;
        CameraOrthoSize = cameraOrthoSize;
    }
}