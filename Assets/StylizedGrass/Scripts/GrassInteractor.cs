using UnityEngine;

public class GrassInteractor : MonoBehaviour
{
    public float radius = .1f;

    private void OnDrawGizmos()
    {
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
